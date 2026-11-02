// Load test for the two report endpoints: MODE=portfolio|project, default 50 VUs for 30 s.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const TOKEN = __ENV.API_TOKEN;
const MODE = __ENV.MODE || 'portfolio';
const PROJECT_ID = __ENV.PROJECT_ID;

export const options = {
  vus: Number(__ENV.VUS || 50),
  duration: __ENV.DURATION || '30s',
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<400'],
  },
};

export function setup() {
  if (!TOKEN) throw new Error('Set API_TOKEN');
  if (MODE === 'project' && !PROJECT_ID) throw new Error('Set PROJECT_ID');
}

export default function () {
  const route = MODE === 'project'
    ? `/api/v1/projects/${PROJECT_ID}/reports/budget-vs-actual`
    : '/api/v1/reports/portfolio-summary?page=1&pageSize=100';
  const response = http.get(`${BASE_URL}${route}`, {
    headers: { Authorization: `Bearer ${TOKEN}` },
    tags: { endpoint: MODE },
  });
  check(response, { 'status is 200': (r) => r.status === 200 });
}

// Writes the full summary to SUMMARY_FILE and one result line to stdout.
export function handleSummary(data) {
  const d = data.metrics.http_req_duration.values;
  const line = `${MODE}: p95=${d['p(95)'].toFixed(1)}ms p50=${d.med.toFixed(1)}ms requests=${data.metrics.http_reqs.values.count} failed=${(data.metrics.http_req_failed.values.rate * 100).toFixed(2)}%\n`;
  return {
    stdout: line,
    [__ENV.SUMMARY_FILE || 'summary.json']: JSON.stringify(data, null, 2),
  };
}
