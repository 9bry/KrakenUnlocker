import { serve } from "https://deno.land/std@0.168.0/http/server.ts"
import { createClient } from "https://esm.sh/@supabase/supabase-js@2"

const supabase = createClient(
  Deno.env.get('SUPABASE_URL')!,
  Deno.env.get('SUPABASE_SERVICE_ROLE_KEY')!
)

const MAX_MACHINES = 3
const SENDER = { name: 'Kraken Xbox Unlocker', email: 'hibernate@digital-kingdom.xyz' }

serve(async (req) => {
  const url = new URL(req.url)
  const action = url.searchParams.get('action')

  // ── Ko-fi webhook ─────────────────────────────────────────────────────────
  // Ko-fi POSTs application/x-www-form-urlencoded with a JSON "data" field.
  // Pricing: $15 = 1 month, $30 = 3 months, $50 = lifetime
  // Any other amount is ignored (no access granted).
  // Webhook URL: .../functions/v1/kraken-auth?action=kofi-webhook
  if (req.method === 'POST' && (action === 'kofi-webhook' || url.pathname.includes('kofi-webhook'))) {
    try {
      const data = await parseKofiPayload(req)
      const email = data.email?.toLowerCase()
      if (!email) return new Response('OK', { status: 200 })

      const amount = parseFloat(String(data.amount ?? '0'))
      let expiresAt: string | null = null

      if (amount === 15) {
        const d = new Date(); d.setMonth(d.getMonth() + 1)
        expiresAt = d.toISOString()
      } else if (amount === 30) {
        const d = new Date(); d.setMonth(d.getMonth() + 3)
        expiresAt = d.toISOString()
      } else if (amount === 50) {
        expiresAt = null
      } else {
        console.error(`Ko-fi webhook: unrecognized amount $${amount} from ${email}, tx=${data.kofi_transaction_id}`)
        return new Response('OK', { status: 200 })
      }

      await supabase.from('licenses').upsert({
        email,
        is_active: true,
        expires_at: expiresAt,
        kofi_transaction_id: data.kofi_transaction_id,
        activated_at: new Date().toISOString(),
        last_verified: new Date().toISOString()
      }, { onConflict: 'email' })
    } catch (e) {
      console.error('Ko-fi webhook error:', e)
    }
    return new Response('OK', { status: 200 })
  }

  // ── Legacy send-code route (old app builds called this directly) ──────────
  // URL: .../functions/v1/kraken-auth?action=legacy-send&email=...&code=...
  if (action === 'legacy-send') {
    const email = url.searchParams.get('email')?.toLowerCase()
    const code  = url.searchParams.get('code')
    if (!email || !code) return json({ error: 'Missing email or code' }, 400)

    const sent = await sendBrevoEmail(email, code)
    if (!sent.ok) return json({ error: sent.error ?? 'Failed to send email.' }, 502)
    return json({ sent: true })
  }

  // ── Send login code ───────────────────────────────────────────────────────
  if (action === 'send-code') {
    const email = url.searchParams.get('email')?.toLowerCase()
    if (!email) return json({ error: 'Missing email' }, 400)

    const { data: license } = await supabase
      .from('licenses')
      .select('email')
      .eq('email', email)
      .eq('is_active', true)
      .maybeSingle()

    if (!license) return json({ error: 'No active license found for that email.' }, 403)

    const code = Math.floor(100000 + Math.random() * 900000).toString()
    const expires = new Date(Date.now() + 30 * 60 * 1000).toISOString()

    // Send email first — don't let a DB issue block delivery
    const sent = await sendBrevoEmail(email, code)
    if (!sent.ok) return json({ error: sent.error ?? 'Failed to send email. Try again shortly.' }, 502)

    // Best-effort DB writes after email succeeds
    const { error: markErr } = await supabase.from('email_codes')
      .update({ used: true })
      .eq('email', email)
      .eq('used', false)
    if (markErr) console.error('Failed to mark old codes:', markErr)

    const { error: insertErr } = await supabase.from('email_codes').insert({ email, code, expires_at: expires, used: false })
    if (insertErr) console.error('Failed to insert code:', insertErr)

    await supabase.from('licenses').update({
      last_code: code,
      last_code_at: new Date().toISOString()
    }).eq('email', email)

    return json({ sent: true })
  }

  // ── Verify code ───────────────────────────────────────────────────────────
  if (action === 'verify-code') {
    const email     = url.searchParams.get('email')?.toLowerCase()
    const code      = url.searchParams.get('code')
    const machineId = url.searchParams.get('machine_id')

    if (!email || !code) return json({ error: 'Missing params' }, 400)

    console.error(`Verify-code for email=${email} code=${code} machine=${machineId}`)

    const { data: codeRows } = await supabase
      .from('email_codes')
      .select('id, expires_at')
      .eq('email', email)
      .eq('code', code)
      .eq('used', false)
      .order('expires_at', { ascending: false })
      .limit(1)

    const codeRow = codeRows?.[0]
    if (!codeRow) {
      console.error(`Code NOT found for ${email} — checking all codes for this email...`)
      const { data: allCodes } = await supabase.from('email_codes').select('code, used, expires_at').eq('email', email).order('expires_at', { ascending: false })
      console.error(`All codes for ${email}: ${JSON.stringify(allCodes)}`)
      return json({ error: 'Invalid or expired code.' }, 403)
    }

    if (new Date(codeRow.expires_at) < new Date()) {
      console.error(`Code expired for ${email}`)
      return json({ error: 'Code has expired. Request a new one.' }, 403)
    }

    const { data: license, error: licErr } = await supabase
      .from('licenses')
      .select('expires_at, machine_ids, is_active')
      .eq('email', email)
      .eq('is_active', true)
      .maybeSingle()

    if (licErr) console.error('License query error:', licErr)

    if (!license) {
      console.error(`License NOT found for ${email}`)
      return json({ error: 'No active license found for that email. Make sure you purchased at ko-fi.com/bryyz.' }, 403)
    }

    if (license.expires_at && new Date(license.expires_at) < new Date()) {
      return json({ error: 'Your license has expired.' }, 403)
    }

    let machineIds: string[] = license.machine_ids ?? []
    if (machineId && !machineIds.includes(machineId)) {
      if (machineIds.length >= MAX_MACHINES) {
        return json({
          error: `This license is already active on ${MAX_MACHINES} devices. Remove a device to add this one.`
        }, 403)
      }
      machineIds = [...machineIds, machineId]
      await supabase.from('licenses').update({ machine_ids: machineIds }).eq('email', email)
    }

    await supabase.from('email_codes').update({ used: true }).eq('id', codeRow.id)
    await supabase.from('licenses').update({ last_verified: new Date().toISOString() }).eq('email', email)

    return json({
      success: true,
      expires_at: license.expires_at ?? null,
    })
  }

  // ── Check saved session ───────────────────────────────────────────────────
  if (action === 'check-session') {
    const email     = url.searchParams.get('email')?.toLowerCase()
    const machineId = url.searchParams.get('machine_id')

    if (!email) return json({ error: 'Missing email' }, 400)

    const { data: license } = await supabase
      .from('licenses')
      .select('is_active, expires_at, machine_ids')
      .eq('email', email)
      .eq('is_active', true)
      .maybeSingle()

    if (!license) return json({ valid: false }, 200)

    const machineIds: string[] = license.machine_ids ?? []
    if (machineId && machineIds.length > 0 && !machineIds.includes(machineId)) {
      return json({ valid: false, error: 'Device not registered for this license.' }, 200)
    }

    return json({ valid: true, expires_at: license.expires_at ?? null })
  }

  return json({ error: 'Unknown action' }, 400)
})

async function parseKofiPayload(req: Request): Promise<Record<string, unknown>> {
  const contentType = req.headers.get('content-type') ?? ''

  if (contentType.includes('application/json')) {
    const body = await req.json()
    if (typeof body.data === 'string') return JSON.parse(body.data)
    return body.data ?? body
  }

  const form = await req.formData()
  const raw = form.get('data')?.toString()
  if (!raw) throw new Error('Missing Ko-fi data field')
  return JSON.parse(raw)
}

async function sendBrevoEmail(email: string, code: string): Promise<{ ok: boolean; error?: string }> {
  const brevoKey = Deno.env.get('BREVO_API_KEY')
  if (!brevoKey) {
    console.error('BREVO_API_KEY is not set')
    return { ok: false, error: 'Email service is not configured.' }
  }

  const resp = await fetch('https://api.brevo.com/v3/smtp/email', {
    method: 'POST',
    headers: { 'api-key': brevoKey, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      sender: SENDER,
      to: [{ email }],
      subject: 'Your Kraken login code',
      htmlContent: `
        <div style="background:#0D0D0D;color:white;padding:32px;font-family:sans-serif;max-width:480px;margin:0 auto;border:1px solid #CC0000;border-radius:12px;">
          <h2 style="color:#CC0000;margin:0 0 8px">KRAKEN XBOX UNLOCKER</h2>
          <p style="color:#888;margin:0 0 24px">Your login verification code</p>
          <div style="background:#1A0505;border:1px solid #CC0000;border-radius:8px;padding:24px;text-align:center;margin-bottom:24px;">
            <span style="font-size:36px;font-weight:bold;letter-spacing:8px;color:#FF0033">${code}</span>
          </div>
          <p style="color:#666;font-size:13px">This code expires in 30 minutes. Do not share it.</p>
        </div>
      `
    })
  })

  if (!resp.ok) {
    const detail = await resp.text()
    console.error('Brevo error:', resp.status, detail)
    return { ok: false, error: 'Email provider rejected the send. Check sender domain in Brevo.' }
  }

  return { ok: true }
}

function json(data: object, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' }
  })
}
