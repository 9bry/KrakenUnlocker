// Backward-compatible wrapper for older app builds that call /functions/v1/send-code directly.
// Forwards to kraken-auth legacy-send handler.
import { serve } from "https://deno.land/std@0.168.0/http/server.ts"

const BREVO_KEY = () => Deno.env.get('BREVO_API_KEY') ?? ''
const SENDER = { name: 'Kraken Xbox Unlocker', email: 'hibernate@digital-kingdom.xyz' }

serve(async (req) => {
  const url = new URL(req.url)
  const email = url.searchParams.get('email')?.toLowerCase()
  const code  = url.searchParams.get('code')

  if (!email || !code) {
    return new Response(JSON.stringify({ error: 'Missing email or code' }), {
      status: 400,
      headers: { 'Content-Type': 'application/json' }
    })
  }

  const key = BREVO_KEY()
  if (!key) {
    return new Response(JSON.stringify({ error: 'Email service is not configured.' }), {
      status: 502,
      headers: { 'Content-Type': 'application/json' }
    })
  }

  const resp = await fetch('https://api.brevo.com/v3/smtp/email', {
    method: 'POST',
    headers: { 'api-key': key, 'Content-Type': 'application/json' },
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
    return new Response(JSON.stringify({ error: 'Failed to send email.' }), {
      status: 502,
      headers: { 'Content-Type': 'application/json' }
    })
  }

  return new Response(JSON.stringify({ sent: true }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' }
  })
})
