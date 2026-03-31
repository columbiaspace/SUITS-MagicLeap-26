/**
 * Vercel serverless proxy: forwards POST /api/generate to your Ollama instance.
 *
 * In Vercel → Project → Settings → Environment Variables, set:
 *   OLLAMA_BASE_URL = http://YOUR_PUBLIC_OR_TUNNELED_HOST:11434
 * (Vercel cannot reach private LAN IPs like 10.x.x.x unless you use a tunnel
 *  or a host reachable from the public internet.)
 */
export default async function handler(req, res) {
  if (req.method !== "POST") {
    res.setHeader("Allow", "POST");
    return res.status(405).json({ error: "Method Not Allowed" });
  }

  const base = process.env.OLLAMA_BASE_URL;
  if (!base || typeof base !== "string") {
    return res
      .status(500)
      .json({ error: "OLLAMA_BASE_URL is not set in Vercel environment" });
  }

  const trimmed = base.replace(/\/+$/, "");
  const target = `${trimmed}/api/generate`;

  let bodyString;
  if (typeof req.body === "string") {
    bodyString = req.body;
  } else if (req.body && typeof req.body === "object") {
    bodyString = JSON.stringify(req.body);
  } else {
    bodyString = "{}";
  }

  try {
    const upstream = await fetch(target, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: bodyString,
    });

    const text = await upstream.text();
    res.status(upstream.status);
    const ct = upstream.headers.get("content-type");
    if (ct) {
      res.setHeader("Content-Type", ct);
    } else {
      res.setHeader("Content-Type", "application/json");
    }
    return res.send(text);
  } catch (err) {
    console.error("[luna-vercel-proxy] upstream fetch failed", err);
    return res.status(502).json({
      error: "Bad gateway: could not reach Ollama",
      detail: String(err && err.message ? err.message : err),
    });
  }
}
