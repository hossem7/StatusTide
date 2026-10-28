import type { StatusConnection } from '../api/statusConnection'
import type { VendorCard } from '../api/types'

/** A StatusConnection the test drives by hand: emit updates, drop and restore the link. */
export class FakeConnection implements StatusConnection {
  started = 0
  stopped = 0
  failStart = false
  private updated: ((card: VendorCard) => void)[] = []
  private reconnecting: (() => void)[] = []
  private reconnected: (() => void)[] = []
  private closed: (() => void)[] = []

  start = async () => {
    this.started++
    if (this.failStart) throw new Error('negotiation failed')
  }

  stop = async () => {
    this.stopped++
  }

  onVendorUpdated = (handler: (card: VendorCard) => void) => void this.updated.push(handler)
  onReconnecting = (handler: () => void) => void this.reconnecting.push(handler)
  onReconnected = (handler: () => void) => void this.reconnected.push(handler)
  onClose = (handler: () => void) => void this.closed.push(handler)

  emitUpdate(card: VendorCard) {
    this.updated.forEach((handler) => handler(card))
  }

  drop() {
    this.reconnecting.forEach((handler) => handler())
  }

  restore() {
    this.reconnected.forEach((handler) => handler())
  }

  close() {
    this.closed.forEach((handler) => handler())
  }
}
