import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/client'
import type { Me } from '../../api/types'
import { account } from '../../test/sampleCards'
import { AccountForm, type AccountFormMode } from './AccountForm'
import { ConfirmEmailPage } from './ConfirmEmailPage'
import { EmailBanner } from './EmailBanner'

function renderForm(mode: AccountFormMode, result: () => Promise<Me>, isGuest = false) {
  const spies = { submit: vi.fn(result), onDone: vi.fn(), onSwitch: vi.fn(), onCancel: vi.fn() }
  render(
    <AccountForm
      mode={mode}
      isGuest={isGuest}
      onDone={spies.onDone}
      onCancel={spies.onCancel}
      onSwitch={spies.onSwitch}
      submit={{ signUp: spies.submit, signIn: spies.submit }}
    />,
  )
  return spies
}

async function fillAndSend(button: string) {
  await userEvent.type(screen.getByLabelText('Email'), 'someone@acme.example')
  await userEvent.type(screen.getByLabelText('Password'), 'correct-horse-battery')
  await userEvent.click(screen.getByRole('button', { name: button }))
}

describe('AccountForm', () => {
  it('sends the email and password, then hands over the user', async () => {
    const { submit, onDone } = renderForm('signIn', async () => account)

    await fillAndSend('Sign in')

    expect(submit).toHaveBeenCalledWith('someone@acme.example', 'correct-horse-battery')
    await waitFor(() => expect(onDone).toHaveBeenCalledWith(account))
  })

  it.each<[AccountFormMode, string, number, string]>([
    ['signIn', 'Sign in', 401, 'Wrong email or password.'],
    ['signIn', 'Sign in', 429, 'Too many tries'],
    ['signIn', 'Sign in', 500, "We couldn't reach the server"],
    ['signUp', 'Create account', 409, 'That email already has an account'],
    ['signUp', 'Create account', 400, 'at least 10 characters'],
  ])('in %s mode, the %s button explains a %i', async (mode, button, code, message) => {
    const { onDone } = renderForm(mode, () => Promise.reject(new ApiError(code, 'POST', '/api/auth')))

    await fillAndSend(button)

    expect(await screen.findByRole('alert')).toHaveTextContent(message)
    expect(onDone).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: button })).toBeEnabled()
  })

  it('offers the right browser password help', () => {
    renderForm('signUp', async () => account)

    expect(screen.getByLabelText('Password')).toHaveAttribute('autocomplete', 'new-password')
    expect(screen.getByLabelText('Password')).toHaveAttribute('minlength', '10')
  })

  it('mentions keeping the dashboard only to a guest', () => {
    renderForm('signUp', async () => account, true)

    expect(screen.getByText('Your dashboard and followed services come with you.')).toBeInTheDocument()
  })

  it('switches between signing up and signing in', async () => {
    const { onSwitch } = renderForm('signUp', async () => account)

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(onSwitch).toHaveBeenCalledWith('signIn')
  })
})

describe('ConfirmEmailPage', () => {
  it('waits for a click, so a mail scanner opening the link changes nothing', () => {
    const confirm = vi.fn(async () => {})
    render(<ConfirmEmailPage token="abc" confirm={confirm} />)

    expect(confirm).not.toHaveBeenCalled()
  })

  it('confirms on click', async () => {
    const confirm = vi.fn(async () => {})
    render(<ConfirmEmailPage token="abc" confirm={confirm} />)

    await userEvent.click(screen.getByRole('button', { name: 'Confirm my email' }))

    expect(confirm).toHaveBeenCalledWith('abc')
    expect(await screen.findByRole('link', { name: 'Go to your dashboard' })).toHaveAttribute('href', '/')
  })

  it('says when the link no longer works', async () => {
    render(<ConfirmEmailPage token="abc" confirm={() => Promise.reject(new ApiError(400, 'POST', '/x'))} />)

    await userEvent.click(screen.getByRole('button', { name: 'Confirm my email' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('expired or was already used')
  })

  it('lets the person try again after a network failure', async () => {
    render(<ConfirmEmailPage token="abc" confirm={() => Promise.reject(new TypeError('Failed to fetch'))} />)

    await userEvent.click(screen.getByRole('button', { name: 'Confirm my email' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("couldn't reach the server")
    expect(screen.getByRole('button', { name: 'Confirm my email' })).toBeEnabled()
  })

  it('treats a link with no token as not working', () => {
    render(<ConfirmEmailPage token={null} />)

    expect(screen.getByRole('alert')).toHaveTextContent('expired or was already used')
  })
})

describe('EmailBanner', () => {
  it('names the address and sends again once', async () => {
    const resend = vi.fn(async () => {})
    render(<EmailBanner email="someone@acme.example" resend={resend} />)
    expect(screen.getByRole('region', { name: 'Email confirmation' })).toHaveTextContent('someone@acme.example')

    await userEvent.click(screen.getByRole('button', { name: 'Send it again' }))

    expect(await screen.findByRole('status')).toHaveTextContent("We've sent it")
    expect(screen.getByRole('button', { name: 'Send it again' })).toBeDisabled()
    expect(resend).toHaveBeenCalledOnce()
  })

  it.each([
    [429, 'Too many tries'],
    [500, "couldn't send that"],
  ])('explains a %i and allows another try', async (code, message) => {
    render(<EmailBanner email="someone@acme.example" resend={() => Promise.reject(new ApiError(code, 'POST', '/x'))} />)

    await userEvent.click(screen.getByRole('button', { name: 'Send it again' }))

    expect(await screen.findByRole('status')).toHaveTextContent(message)
    expect(screen.getByRole('button', { name: 'Send it again' })).toBeEnabled()
  })
})
