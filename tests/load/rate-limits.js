// Load test: do the rate limits hold when many requests arrive at once?
//
// Runs against the Compose stack. Every request comes from one address, so each scenario
// needs a fresh minute of limits: wait a minute between runs. Steps are in tests/load/README.md.
//
//   SCENARIO=address  300 requests from 50 parallel users with no session. Exactly the
//                     address limit (120 a minute) may get through; the rest must be 429s
//                     with a Retry-After header.
//   SCENARIO=lockout  20 parallel wrong passwords for one email. The per-address limit lets
//                     10 reach the sign-in check, and the account lock must let exactly 5 of
//                     those be checked, even though they all arrive together.

import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5080';
const SCENARIO = __ENV.SCENARIO || 'address';

// Keep in step with RateLimitingExtensions, AccountsExtensions and SignInLockout.
const ADDRESS_LIMIT = 120;
const LOCKOUT_ATTEMPTS = 5;

const allowed = new Counter('allowed');
const limited = new Counter('limited');
const unexpected = new Counter('unexpected');

const scenarios = {
  address: {
    scenarios: { burst: { executor: 'shared-iterations', vus: 50, iterations: 300, maxDuration: '30s' } },
    thresholds: {
      allowed: [`count==${ADDRESS_LIMIT}`],
      limited: [`count==${300 - ADDRESS_LIMIT}`],
      unexpected: ['count==0'],
    },
  },
  lockout: {
    scenarios: { burst: { executor: 'shared-iterations', vus: 20, iterations: 20, maxDuration: '30s' } },
    thresholds: {
      allowed: [`count==${LOCKOUT_ATTEMPTS}`],
      limited: [`count==${20 - LOCKOUT_ATTEMPTS}`],
      unexpected: ['count==0'],
    },
  },
};

export const options = { ...scenarios[SCENARIO], summaryTrendStats: ['p(50)', 'p(95)', 'max'] };

export function setup() {
  return { email: `load-${Date.now()}@acme.example` };
}

export default function (data) {
  const response =
    SCENARIO === 'lockout'
      ? http.post(
          `${BASE_URL}/api/auth/login`,
          JSON.stringify({ email: data.email, password: 'not-the-password' }),
          { headers: { 'Content-Type': 'application/json' } },
        )
      : http.get(`${BASE_URL}/api/me`);

  // A 401 means the request got past every limit and was judged on its merits.
  if (response.status === 401) {
    allowed.add(1);
  } else if (response.status === 429) {
    limited.add(1);
    check(response, { 'a 429 says when to retry': (r) => Number(r.headers['Retry-After']) >= 1 });
  } else {
    unexpected.add(1);
  }
}
