import { useCallback, useEffect, useRef, useState } from 'react'
import { api, ApiError } from '../../api/client'
import type { Catalog, CatalogQuery, CatalogVendor, CatalogVendorDetail, VendorCategory } from '../../api/types'

const searchDelay = 300

export type FollowFailure = 'limit' | 'gone' | 'changed' | 'tooMany' | 'unreachable'

export interface Picker {
  vendorId: string
  vendorName: string
  /** Null while the components load. */
  detail: CatalogVendorDetail | null
}

export interface UseCatalog {
  /** Null until the first load finishes. Keeps the last list while a new search loads. */
  vendors: CatalogVendor[] | null
  /** True when the last load failed. */
  loadFailed: boolean
  retry: () => void
  search: string
  setSearch: (search: string) => void
  category: VendorCategory | null
  setCategory: (category: VendorCategory | null) => void
  picker: Picker | null
  /** True while a follow change or the picker's load is on its way. */
  busy: boolean
  failure: { vendorId: string; vendorName: string; reason: FollowFailure } | null
  /** The latest change in words, for the screen reader announcement. */
  announcement: string
  follow: (vendor: CatalogVendor) => void
  openPicker: (vendor: CatalogVendor) => void
  closePicker: () => void
  saveComponents: (componentIds: string[]) => void
  unfollow: () => void
}

interface Options {
  /** Called when a request shows the session has ended. */
  onSignedOut?: () => void
  load?: (query: CatalogQuery, signal: AbortSignal) => Promise<Catalog>
  loadVendor?: (vendorId: string, signal: AbortSignal) => Promise<CatalogVendorDetail>
  setFollows?: (vendorId: string, componentIds: string[] | null) => Promise<CatalogVendorDetail>
  removeFollows?: (vendorId: string) => Promise<void>
}

function reasonFor(e: unknown): FollowFailure {
  if (e instanceof ApiError) {
    if (e.status === 409) return 'limit'
    if (e.status === 404) return 'gone'
    if (e.status === 400) return 'changed'
    if (e.status === 429) return 'tooMany'
  }
  return 'unreachable'
}

function withFollowCounts(vendor: CatalogVendor, detail: CatalogVendorDetail): CatalogVendor {
  return {
    ...vendor,
    componentCount: detail.components.length,
    followedComponentCount: detail.components.filter((c) => c.followed).length,
  }
}

/**
 * Loads the catalog and changes what the user follows. Each change updates its tile straight away
 * and then loads the list again, which also replaces any older load still on its way that would
 * otherwise put the old counts back.
 */
export function useCatalog({
  onSignedOut,
  load = api.getCatalog,
  loadVendor = api.getCatalogVendor,
  setFollows = api.setFollows,
  removeFollows = api.unfollow,
}: Options = {}): UseCatalog {
  const [vendors, setVendors] = useState<CatalogVendor[] | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const [search, setSearch] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [category, setCategory] = useState<VendorCategory | null>(null)
  const [reloads, setReloads] = useState(0)
  const [picker, setPicker] = useState<Picker | null>(null)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<UseCatalog['failure']>(null)
  const [announcement, setAnnouncement] = useState('')
  const pickerRequest = useRef<AbortController | null>(null)

  const reload = useCallback(() => setReloads((n) => n + 1), [])

  const isSignedOut = useCallback(
    (e: unknown) => {
      if (!(e instanceof ApiError && e.status === 401)) return false
      onSignedOut?.()
      return true
    },
    [onSignedOut],
  )

  useEffect(() => {
    const timer = setTimeout(() => setAppliedSearch(search.trim()), searchDelay)
    return () => clearTimeout(timer)
  }, [search])

  useEffect(() => {
    const controller = new AbortController()
    load({ search: appliedSearch, category }, controller.signal)
      .then((catalog) => {
        if (controller.signal.aborted) return
        setVendors(catalog.vendors)
        setLoadFailed(false)
      })
      .catch((e: unknown) => {
        if (controller.signal.aborted || isSignedOut(e)) return
        setLoadFailed(true)
      })
    return () => controller.abort()
  }, [appliedSearch, category, reloads, load, isSignedOut])

  useEffect(() => () => pickerRequest.current?.abort(), [])

  const showDetail = useCallback((detail: CatalogVendorDetail) => {
    setVendors((list) => list?.map((v) => (v.id === detail.id ? withFollowCounts(v, detail) : v)) ?? null)
  }, [])

  const fail = useCallback(
    (e: unknown, vendorId: string, vendorName: string) => {
      if (isSignedOut(e)) return
      const reason = reasonFor(e)
      setFailure({ vendorId, vendorName, reason })
      // Gone from the catalog, so its tile and picker should go too.
      if (reason === 'gone') {
        setPicker(null)
        reload()
      }
    },
    [isSignedOut, reload],
  )

  const loadPicker = useCallback(
    (vendorId: string, vendorName: string) => {
      pickerRequest.current?.abort()
      const controller = new AbortController()
      pickerRequest.current = controller
      setPicker({ vendorId, vendorName, detail: null })
      setBusy(true)
      loadVendor(vendorId, controller.signal)
        .then((detail) => {
          if (controller.signal.aborted) return
          setPicker({ vendorId, vendorName, detail })
          showDetail(detail)
        })
        .catch((e: unknown) => {
          if (controller.signal.aborted) return
          setPicker(null)
          fail(e, vendorId, vendorName)
        })
        .finally(() => {
          if (!controller.signal.aborted) setBusy(false)
        })
    },
    [loadVendor, showDetail, fail],
  )

  const follow = useCallback(
    ({ id, name }: CatalogVendor) => {
      pickerRequest.current?.abort()
      setPicker(null)
      setBusy(true)
      setFailure(null)
      setFollows(id, null)
        .then((detail) => {
          showDetail(detail)
          reload()
          // Following everything first means one click is enough; the picker narrows it down.
          setPicker({ vendorId: id, vendorName: name, detail })
          setAnnouncement(`Following ${name}. Choose the components you use.`)
        })
        .catch((e: unknown) => fail(e, id, name))
        .finally(() => setBusy(false))
    },
    [setFollows, showDetail, reload, fail],
  )

  const openPicker = useCallback(
    ({ id, name }: CatalogVendor) => {
      setFailure(null)
      loadPicker(id, name)
    },
    [loadPicker],
  )

  const closePicker = useCallback(() => {
    pickerRequest.current?.abort()
    setPicker(null)
    setBusy(false)
    setFailure(null)
  }, [])

  const saveComponents = useCallback(
    (componentIds: string[]) => {
      if (!picker) return
      const { vendorId, vendorName } = picker
      setBusy(true)
      setFailure(null)
      setFollows(vendorId, componentIds)
        .then((detail) => {
          showDetail(detail)
          reload()
          setPicker(null)
          setAnnouncement(`Saved your components for ${vendorName}.`)
        })
        .catch((e: unknown) => {
          fail(e, vendorId, vendorName)
          // A component was removed since the picker loaded, so show the current list.
          if (reasonFor(e) === 'changed') loadPicker(vendorId, vendorName)
        })
        .finally(() => setBusy(false))
    },
    [picker, setFollows, showDetail, reload, fail, loadPicker],
  )

  const unfollow = useCallback(() => {
    if (!picker) return
    const { vendorId, vendorName } = picker
    setBusy(true)
    setFailure(null)
    removeFollows(vendorId)
      .then(() => {
        setVendors((list) => list?.map((v) => (v.id === vendorId ? { ...v, followedComponentCount: 0 } : v)) ?? null)
        reload()
        setPicker(null)
        setAnnouncement(`Stopped following ${vendorName}.`)
      })
      .catch((e: unknown) => fail(e, vendorId, vendorName))
      .finally(() => setBusy(false))
  }, [picker, removeFollows, reload, fail])

  return {
    vendors,
    loadFailed,
    retry: reload,
    search,
    setSearch,
    category,
    setCategory,
    picker,
    busy,
    failure,
    announcement,
    follow,
    openPicker,
    closePicker,
    saveComponents,
    unfollow,
  }
}
