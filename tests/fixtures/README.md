# Test fixtures

Sample vendor responses used by the reader tests, so no test ever calls a real vendor.

These files are written by hand in each format's documented shape, for made-up vendors on `.example` domains. They are not copies of real responses: real ones contain the vendor's written update text, which StatusTide never stores, and recording them would mean polling a vendor before it passes verification.

The update `body` fields hold placeholder text on purpose. The tests check that it never reaches the parsed result.

| Folder | Format |
| --- | --- |
| `statuspage/` | Atlassian Statuspage `/api/v2/summary.json` |
