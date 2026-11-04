# Load tests

[k6](https://grafana.com/docs/k6/) scripts that check the rate limits hold under parallel load. The integration tests send requests one at a time; these send them all at once, which is where a limiter that reads and then writes its count would let extra requests through.

## Run

Start the stack with a fresh API image, then run each scenario through Docker, so nothing needs installing:

```bash
docker compose up -d --build

docker run --rm -i -e SCENARIO=address grafana/k6:2.2.0 run - < tests/load/rate-limits.js
# wait a minute, so the address limit starts over
docker run --rm -i -e SCENARIO=lockout grafana/k6:2.2.0 run - < tests/load/rate-limits.js
```

k6 exits with a non-zero code when a threshold fails. On Linux, add `--add-host=host.docker.internal:host-gateway`.

| Scenario | Sends | Passes when |
| --- | --- | --- |
| `address` | 300 requests from 50 parallel users with no session | exactly 120 get through, 180 get a 429 with Retry-After |
| `lockout` | 20 parallel wrong passwords for one email | exactly 5 are checked, 15 get a 429 (10 from the address limit, 5 from the account lock) |

Every request comes from your machine's address, so the scenarios share one budget. Limits are kept in Redis and survive an API restart, so restarting doesn't reset them; waiting a minute does.
