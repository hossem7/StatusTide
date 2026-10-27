import type { DashboardData } from './types'
import rawData from './sampleData.json'

// Placeholder data for building the dashboard UI before the API exists.
// The JSON import stands in for a fetch; a later step swaps this for a real
// API call that returns the same DashboardData shape (issue #7).
export const sampleData = rawData as DashboardData
