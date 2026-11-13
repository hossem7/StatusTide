import { useEffect } from 'react'
import { ApiError } from '../../api/client'
import type { Incident, IncidentChecks } from '../../api/types'
import { AppLink } from '../../components/AppLink'
import { StatusPill } from '../../components/StatusPill'
import { externalUrl } from '../catalog/catalogLabels'
import { formatTime } from '../dashboard/format'
import { useNow } from '../dashboard/useNow'
import { CheckChart } from './CheckChart'
import {
  checksHeadline,
  durationText,
  failureText,
  incidentStatusLabel,
  sameMoment,
  shortVendorName,
  startOf,
  worstStatus,
} from './incidentFormat'
import { useIncident, type IncidentState } from './useIncident'
import './Incident.css'

interface PageProps {
  /** Null when the address has no valid id, which shows "not found" without asking the server. */
  incidentId: string | null
  onSignedOut: () => void
}

export function IncidentPage({ incidentId, onSignedOut }: PageProps) {
  if (incidentId === null) {
    return <IncidentView state={{ kind: 'error', error: new ApiError(404, 'GET', '/api/incidents') }} retry={() => {}} now={0} />
  }
  return <LoadedIncidentPage incidentId={incidentId} onSignedOut={onSignedOut} />
}

function LoadedIncidentPage({ incidentId, onSignedOut }: { incidentId: string; onSignedOut: () => void }) {
  const { state, retry } = useIncident(incidentId)
  const now = useNow(30_000)

  const signedOut = state.kind === 'error' && state.error instanceof ApiError && state.error.status === 401
  useEffect(() => {
    if (signedOut) onSignedOut()
  }, [signedOut, onSignedOut])

  return <IncidentView state={state} retry={retry} now={now} />
}

/** The screen itself, with no requests or timers, so tests can render any state directly. */
export function IncidentView({ state, retry, now }: { state: IncidentState; retry: () => void; now: number }) {
  return (
    <main className="incident">
      <AppLink href="/" className="incident__back">
        ← Back to dashboard
      </AppLink>
      {state.kind === 'loading' && <p className="incident__message">Loading the incident...</p>}
      {state.kind === 'error' && <LoadError error={state.error} onRetry={retry} />}
      {state.kind === 'ready' && <IncidentDetails incident={state.incident} now={now} />}
    </main>
  )
}

function LoadError({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  if (error instanceof ApiError && error.status === 404) {
    return (
      <div className="incident__message" role="alert">
        <p>We couldn't find that incident.</p>
      </div>
    )
  }
  return (
    <div className="incident__message" role="alert">
      <p>We couldn't load the incident.</p>
      <button type="button" className="button" onClick={onRetry}>
        Try again
      </button>
    </div>
  )
}

function IncidentDetails({ incident, now }: { incident: Incident; now: number }) {
  const affected = incident.components.filter((c) => c.isAffected)
  const worst = worstStatus(affected.map((c) => c.status))
  return (
    <>
      <div className="incident__top">
        {/* The vendor's own title: React renders it as text, never as HTML. */}
        <h1>
          {incident.vendor.name}: {incident.title}
        </h1>
        {incident.resolvedAt === null ? (
          worst && <StatusPill status={worst} />
        ) : (
          <span className="status-pill" data-status="operational">
            <span className="status-pill__dot" aria-hidden="true" />
            Resolved
          </span>
        )}
        <span className="incident__when">
          Started {formatTime(startOf(incident))} · {durationText(incident, now)}
        </span>
      </div>

      <div className="incident__row">
        <ChecksSection incident={incident} />
        <ComponentList incident={incident} />
      </div>

      <div className="incident__row">
        <OfficialUpdates incident={incident} />
      </div>
    </>
  )
}

function ChecksSection({ incident }: { incident: Incident }) {
  const vendor = shortVendorName(incident.vendor.name)
  const checks = incident.checks
  if (checks === null) {
    return (
      <section className="incident__panel incident__panel--wide" aria-labelledby="incident-checks">
        <h2 id="incident-checks">Our checks</h2>
        <p className="incident__note">We don't run our own checks on {vendor} yet, so there is nothing to compare.</p>
      </section>
    )
  }

  return (
    <section className="incident__panel incident__panel--wide" aria-labelledby="incident-checks">
      <div className="incident__panel-head">
        <h2 id="incident-checks">{checksHeadline(checks, incident.vendor.name)}</h2>
        <span className="incident__note">From our checks of {vendor}, once a minute</span>
      </div>
      {checks.points.length > 0 ? (
        <>
          <CheckChart checks={checks} />
          <Timeline checks={checks} vendor={vendor} />
          <p className="incident__note">
            Short green bars are passing checks (bar height is response time). Full-height red bars are failed checks.
            {checks.outage && ' The line marks the official post.'}
          </p>
        </>
      ) : (
        <>
          <Timeline checks={checks} vendor={vendor} />
          <p className="incident__note">We keep each check for 7 days, so the chart for this incident is gone.</p>
        </>
      )}
    </section>
  )
}

/** The moments that matter, in order, under the chart. */
function Timeline({ checks, vendor }: { checks: IncidentChecks; vendor: string }) {
  const outage = checks.outage
  if (!outage) return null
  const firstFailure = checks.points.find((p) => sameMoment(p.checkedAt, outage.firstFailedAt))
  const moments = [
    { at: outage.firstFailedAt, kind: 'failed', text: `First failed check, ${failureText(firstFailure)}` },
    { at: outage.detectedAt, kind: 'detected', text: 'Confirmed down after 3 failures in a row' },
    { at: outage.vendorPostedAt, kind: 'post', text: `${vendor} posted on its official status page` },
    ...(outage.recoveredAt ? [{ at: outage.recoveredAt, kind: 'passed', text: 'Our checks passed again' }] : []),
  ].sort((a, b) => Date.parse(a.at) - Date.parse(b.at))

  return (
    <ol className="incident__timeline">
      {moments.map((m) => (
        <li key={m.kind}>
          <span className="incident__swatch" data-kind={m.kind} aria-hidden="true" />
          <span>
            <b>{formatTime(m.at)}</b> {m.text}
          </span>
        </li>
      ))}
    </ol>
  )
}

function ComponentList({ incident }: { incident: Incident }) {
  return (
    <aside className="incident__panel incident__panel--side" aria-labelledby="incident-components">
      <h2 id="incident-components">Components</h2>
      {incident.components.length === 0 ? (
        <p className="incident__note">{shortVendorName(incident.vendor.name)} didn't name any components.</p>
      ) : (
        <ul className="incident__components">
          {incident.components.map((c) => (
            <li key={c.id}>
              <span>
                {c.name}
                <span className="incident__tags">
                  {c.isAffected && <span>Named in this incident</span>}
                  {c.isFollowed && <span>You follow it</span>}
                </span>
              </span>
              <StatusPill status={c.status} />
            </li>
          ))}
        </ul>
      )}
      <p className="incident__note">Statuses are as of now.</p>
    </aside>
  )
}

function OfficialUpdates({ incident }: { incident: Incident }) {
  const vendor = shortVendorName(incident.vendor.name)
  const source = externalUrl(incident.sourceUrl)
  return (
    <section className="incident__panel incident__panel--wide" aria-labelledby="incident-updates">
      <h2 id="incident-updates">Official status from {vendor}</h2>
      {incident.updates.length === 0 ? (
        <p className="incident__note">No status updates yet.</p>
      ) : (
        <ol className="incident__updates">
          {incident.updates.map((u) => (
            <li key={`${u.occurredAt}-${u.status}`}>
              <time dateTime={u.occurredAt}>{formatTime(u.occurredAt)}</time>
              <b>{incidentStatusLabel(u.status)}</b>
            </li>
          ))}
        </ol>
      )}
      {source ? (
        <a className="incident__source" href={source} target="_blank" rel="noopener noreferrer">
          Read the full updates on {vendor}'s status page ↗
        </a>
      ) : (
        incident.vendor.isDemo && (
          <p className="incident__note">Acme Cloud is our demo vendor, so it has no status page of its own.</p>
        )
      )}
    </section>
  )
}
