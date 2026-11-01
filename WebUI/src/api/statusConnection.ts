import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { api } from './client'
import type { VendorCard } from './types'

/**
 * The parts of the SignalR connection the dashboard uses. Kept small so tests can pass a fake
 * that emits updates and disconnects without a server.
 */
export interface StatusConnection {
  start(): Promise<void>
  stop(): Promise<void>
  onVendorUpdated(handler: (card: VendorCard) => void): void
  onReconnecting(handler: () => void): void
  onReconnected(handler: () => void): void
  /** Called when the connection is gone for good, after automatic reconnects gave up. */
  onClose(handler: () => void): void
}

/** Must match StatusHub.VendorUpdated on the server. */
export const VendorUpdated = 'vendorUpdated'

export function createStatusConnection(): StatusConnection {
  const connection = new HubConnectionBuilder()
    // Asked on every connect and reconnect, so a reconnect after a guest signs up uses the new token.
    .withUrl('/hubs/status', { accessTokenFactory: api.getHubToken })
    // Retries after 0, 2, 10 and 30 seconds, then gives up and calls onClose.
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()

  return {
    start: () => connection.start(),
    stop: () => connection.stop(),
    onVendorUpdated: (handler) => connection.on(VendorUpdated, handler),
    onReconnecting: (handler) => connection.onreconnecting(() => handler()),
    onReconnected: (handler) => connection.onreconnected(() => handler()),
    onClose: (handler) => connection.onclose(() => handler()),
  }
}
