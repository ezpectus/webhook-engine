# Incidents & review findings — before / after

Разбор найденного при ревью кода 2026-10-26: что было, почему это
ломалось (или ломалось бы под нагрузкой), и фикс. Каждый фикс также
несёт комментарий «Было: …» прямо рядом с кодом.

Формат: находка → механизм бага → доказательство/сценарий → фикс →
что бы спросили на ревью в нормальной конторе.

---

## I-1 · HttpResponseMessage не диспозился — утечка пула соединений

**Файл:** `src/WebhookEngine.Infrastructure/Delivery/WebhookSender.cs`
**Коммит фикса:** `8265dec` · **Тяжесть:** реальный баг, проявляется
только под нагрузкой

### Было

```csharp
var resp = await http.PostAsync(url, content, ct);
var code = (int)resp.StatusCode;
```

### Почему говно

`HttpClient` держит пул TCP-соединений и переиспользует их между
запросами. Соединение возвращается в пул только когда ответ
**прочитан до конца и disposed**. `resp` без `using` — соединение
повисает до сборщика мусора: GC финализирует когда-нибудь, а сокет
нужен прямо сейчас.

**Сценарий:** на 100 rps не видно вообще. На 1000+ rps пул
исчерпывается → новые send'ы ждут свободный сокет → timeout'ы,
которые в метриках выглядят как «endpoint лёг». Диагностируешь
получателя, а виноват — незакрытый response.

Это классика, за которую на ревью спрашивают «а response кто
диспозит?».

### Стало

```csharp
// Фикс ревью: response обязан быть using'ом. Недиспознутый
// HttpResponseMessage держит соединение в пуле до GC — на
// высоком RPS пул исчерпывается и новые запросы ждут сокет.
// Было: var resp = await http.PostAsync(url, content, ct);
//   — работает на 100 rps, умирает на 1000+.
using var resp = await http.PostAsync(url, content, ct);
var code = (int)resp.StatusCode;
```

---

## I-2 · `RateLimitPerSecond` без валидации — вечный crash-loop

**Файл:** `src/WebhookEngine.Api/Endpoints/Endpoints.cs` (RegisterWebhook)
**Коммит фикса:** `8265dec` · **Тяжесть:** реальный баг — невалидное
проходило на входе и взрывалось в глубине

### Было

```csharp
var hook = new WebhookEndpoint
{
    ...
    RateLimitPerSecond = req.RateLimitPerSecond ?? 10,
    ...
};
```

`?? 10` ловил только `null`. Клиент мог зарегистрировать webhook с
`rateLimitPerSecond: 0` или `-5` — регистрация возвращала 201, всё
выглядело нормально.

### Почему говно

Взрыв — при первой доставке, не при регистрации:

```
POST /webhooks {rate: 0}  → 201 Created (приняли!)
POST /events             → 202 (событие в системе)
worker: limiter.AcquireDelayAsync(rate=0)
  → TokenBucket ctor: ThrowIfNegativeOrZero → THROW
  → воркер ловит Exception → событие остаётся InFlight
  → lease протухает → sweeper возвращает в Pending
  → sweeper claim → enqueue → снова THROW
  → ...вечно
```

Событие не теряется (at-least-once держится), но оно прыгает
InFlight↔Pending бесконечно, каждый круг — легальный «дубль» в
метриках реклеймов и мусор в логах. И так для КАЖДОГО события этого
webhook'а.

Правило, которое уже было принято в проекте («мусор чинит клиент»,
невалидное → 400), здесь нарушалось: пускали больное на входе.

### Стало

```csharp
if (string.IsNullOrWhiteSpace(req.Url) || !Uri.TryCreate(req.Url, UriKind.Absolute, out _))
    return Results.BadRequest(new { error = "valid url required" });
// Фикс ревью: rate <= 0 валиден на входе, но ломается в глубине —
// TokenBucket бросал бы ArgumentOutOfRangeException при каждой
// отправке → событие прыгало InFlight↔Pending вечно (crash-loop,
// дубли-фон). Невалидное отсекаем на границе, как кривой URL.
// Было: RateLimitPerSecond = req.RateLimitPerSecond ?? 10 — без проверки.
if (req.RateLimitPerSecond is <= 0)
    return Results.BadRequest(new { error = "rateLimitPerSecond must be > 0" });
```

**Урок:** валидация — на границе системы, не «по месту взрыва». Чем
дальше невалидное зашло, тем дороже его выковыривать и тем грязнее
состояние.

---

## I-3 · Off-by-one в backoff — первая задержка была 2×base

**Файл:** `src/WebhookEngine.Api/Workers/DeliveryWorker.cs`
**Коммит фикса:** `158fdfa` · **Тяжесть:** семантическая ошибка —
гарантию не ломала, но каденс ретраев врал в 2 раза

### Было

```csharp
tracked.AttemptCount++;                    // стало 1 после первой неудачи
...
tracked.NextAttemptAt = now + backoff.ComputeDelay(tracked.AttemptCount);
```

`ComputeDelay(attempt)` задуман **0-based**: delay = `base·2^attempt`.
А вызывали его с уже-инкрементированным счётчиком — фактически
1-based.

### Почему говно

```
Попытка 1 фейлится → AttemptCount=1 → ComputeDelay(1) = base·2 = 2с
Задокументировано: 1с / 2с / 4с / 8с
Фактически:        2с / 4с / 8с / 16с
```

Гарантию не ломало (cap 5 мин и max 5 попыток работают), но
последовательность была не та, что в спеке. На хаос-прогоне это
маскировалось тиком sweeper'а — поэтому и не заметили глазами.

### Стало

```csharp
tracked.Status = EventStatus.RetryScheduled;
// attemptCount-1: ComputeDelay ждёт 0-based номер попытки,
// а счётчик уже инкрементирован (1-я неудача → 1). Было
// ComputeDelay(AttemptCount) → первая задержка 2·base вместо base.
tracked.NextAttemptAt = now + backoff.ComputeDelay(tracked.AttemptCount - 1);
```

**Урок:** контракт «0-based» в доке функции — это тоже контракт.
Когда счётчик инкрементят до вызова, передавать его напрямую —
типичный off-by-one.

---

## I-4 · Replay: enqueue до commit — stale-окно

**Файл:** `src/WebhookEngine.Api/Endpoints/Endpoints.cs` (ReplayDeadLetter)
**Коммит фикса:** `158fdfa` · **Тяжесть:** край — не потеря, но
задержка до 5 минут на микросекундной гонке

### Было

```csharp
ev.Status = queue.TryEnqueue(ev) ? EventStatus.InFlight : EventStatus.Pending;
ev.AttemptCount = 0;
ev.NextAttemptAt = null;
ev.UpdatedAt = now;
dl.ReplayedAt = now;
await db.SaveChangesAsync(ct);
```

Enqueue — ДО записи в PG.

### Почему говно

Между `TryEnqueue` и `SaveChanges` в PG ещё лежит `DeadLettered`:

```
TryEnqueue (элемент в канале)
    ↓
воркер: FindAsync(ev.Id) → Status == DeadLettered
    → tracked.Status != InFlight → пропустить как stale
    → элемент канала УШЁЛ и потерян
    ↓
SaveChanges → строка теперь InFlight навсегда...
    ...до lease-протухания (5 мин) → sweeper вернёт
```

Потери нет (sweeper подберёт), но replay мог «отработать» с
задержкой до lease-таймаута вместо мгновенно. Окно — микросекунды,
но зачем оно вообще?

### Стало

```csharp
// Фикс ревью: сначала durable-запись, потом enqueue — как в ingest.
// Было: TryEnqueue до SaveChanges → воркер мог подхватить элемент,
// пока в PG ещё DeadLettered, пропустить как stale, а строка
// висла InFlight до lease-протухания (5 мин задержки). Теперь:
// claim InFlight → enqueue → не влезло → откат в Pending.
ev.Status = EventStatus.InFlight;
await db.SaveChangesAsync(ct);
if (!queue.TryEnqueue(ev))
{
    await db.Events
        .Where(e => e.Id == ev.Id && e.Status == EventStatus.InFlight)
        .ExecuteUpdateAsync(s => s
            .SetProperty(e => e.Status, EventStatus.Pending)
            .SetProperty(e => e.UpdatedAt, now), ct);
    ev.Status = EventStatus.Pending;
}
```

**Урок:** durable-first — правило системы («PG = очередь, канал =
ускоритель»), а здесь оно было нарушено. Один и тот же паттерн
«запись → enqueue → откат» теперь един в ingest и replay.

---

## Проверено и чисто (не баги — зафиксировано для полноты)

- **Claim/rollback-гонки (В3):** `claimed == 0` корректно читается как
  «забрал sweeper»; откат только при `Status == InFlight`.
- **Fail-open Redis:** `RedisConnectionException` и
  `RedisTimeoutException` — наследники `RedisException`, ловятся.
- **Heap-версии:** две записи с одной версией невозможны
  (версия инкрементится при каждом schedule), stale-записи режутся
  проверкой `_versions`.
- **`FullMode.Wait` + `TryWrite`:** корректная пара — `TryWrite`
  честно возвращает `false` при полном канале.
- **Shutdown-cancel vs timeout:** различение через
  `OperationCanceledException when !ct.IsCancellationRequested` —
  правильное.

## Осознанные границы (не трогали — решения, не баги)

- **408 не ретраится** — политика «4xx кроме 429» принята осознанно.
- **Bucket не инвалидируется** при смене `RateLimitPerSecond` —
  endpoint'а обновления пока нет.
- **Payload без лимита размера** — для демо ок; в прод — max-size
  middleware.
