export function openBillingSession(value: string) {
  // The link exists only at the response boundary. It is never logged, put in
  // application state, or persisted in browser storage.
  window.location.assign(trustedBillingSessionUrl(value).toString());
}

export function trustedBillingSessionUrl(value: string) {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error("The billing session URL is unavailable.");
  }

  const localHttp = url.protocol === "http:" && ["localhost", "127.0.0.1", "[::1]", "::1"].includes(url.hostname);
  if ((url.protocol !== "https:" && !localHttp) || url.username || url.password)
    throw new Error("The billing session URL is unavailable.");

  return url;
}
