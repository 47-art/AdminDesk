checks 1-4: PASS, provider Hangfire

# Background jobs on SQLite: spike results

Run on 2026-10-08 against the real API host, using `scripts/spike/run-spike.sh` (modes `basic`, `restart`, `soak`). The jobs dashboard is not part of this phase and is not mapped.

## Tested setup

| Item | Value |
|------|-------|
| Hangfire.AspNetCore | 1.8.25 |
| Hangfire.Storage.SQLite | 0.4.3 |
| SQLitePCLRaw.lib.e_sqlite3 | 2.1.12 (explicit reference) |
| Storage file | `hangfire.db`, separate from the application database |
| Connection | full-mutex, busy timeout 10 s |
| Queue poll interval | 2 s |
| Distributed lock lifetime | 2 minutes |
| Worker count | 5 |
| Heartbeat | every 10 s (six-field cron accepted by Hangfire) |

All background work goes through one scheduler interface; `Jobs:Provider` selects `Hangfire` or `Timer` (a hosted service with a `scheduled_jobs` table). An unknown value stops startup and lists the allowed values.

## Results (provider Hangfire)

| Check | What | Measured | Result |
|-------|------|----------|--------|
| check1 | Recurring heartbeat, 3 minutes | 18 heartbeat lines in 180 s (6 per minute, needed at least 6); 180 probe rows from the five writers in the same run | PASS |
| check2 | Delayed job (30 s) | fired exactly once (1 line) | PASS |
| check3 | Restart survival: 60 s delayed job, whole process tree killed 8 s after scheduling, restarted after the due time on the same folder | fired 0 times before the kill, 1 time after restart | PASS |
| check4 | 10-minute soak: 5 write jobs every 5 s, one warning log request per second (database log sink), health traffic | expected 600 probe rows, actual 605; forbidden-pattern matches (database is locked, SQLite Error 5, SQLITE_BUSY, AccessViolation, DistributedLockTimeout): 0; failed jobs: 0; log sink failures: 0; sink warning rows written: 514; process alive at the end | PASS |

## Verdict

All four checks pass with Hangfire, so Hangfire stays the default provider. The timer-based provider remains available behind the same interface; it was verified to start and create its tables, but the soak was not run against it because the Hangfire checks passed.
