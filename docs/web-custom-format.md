# Chromium Web Custom Format (Map Indirection)

This document describes how the project exposes the MIME-like identifier
`web data/my-custom-format` on the Windows clipboard using the Chromium
Web Custom Format Map convention. This is the scheme Chromium-based
browsers use to bridge the Async Clipboard API's custom MIME types to
OS-level clipboard formats.

## The Problem

The Windows clipboard stores data under **registered clipboard formats**,
each identified by a `uint` ID and a display name string. Web standards
(the Async Clipboard API) let pages read and write arbitrary MIME types.
There is no 1:1 mapping — Chromium cannot register an unbounded set of
Windows clipboard formats on demand, and naive producers registering a
format literally named `application/vnd.example` would not be
discoverable by browsers.

## The Chromium Scheme

Chromium reserves two *classes* of registered format names:

| Registered name | Purpose |
|-----------------|---------|
| `Web Custom Format Map` | A JSON object mapping MIME keys → slot names |
| `Web Custom Format0` .. `Web Custom Format15` | Payload slots |

A consumer that wants to read `web data/my-custom-format` from the
clipboard:

1. Reads the `Web Custom Format Map` format.
2. Parses its JSON contents.
3. Looks up the MIME key `web data/my-custom-format`.
4. Finds the value — e.g., `"Web Custom Format0"`.
5. Reads that registered format to get the actual payload bytes.

## What This App Publishes

When the **Custom Format** checkbox is checked and the user clicks Copy,
the app promises two registered clipboard formats using delayed
rendering:

```
Web Custom Format Map        → promised (hData = NULL)
Web Custom Format0           → promised (hData = NULL)
```

On a paste, Windows sends `WM_RENDERFORMAT` twice — once per format.

### Map contents

`Web Custom Format Map` resolves to:

```json
{"data/my-custom-format":"Web Custom Format0"}
```

UTF-8 encoded. **No null terminator.** The byte count on the clipboard
is authoritative; a trailing NUL would break strict JSON parsers reading
the buffer by size.

**Critical: the JSON key is the BARE MIME type, NOT prefixed with
`"web "`.** Chromium prepends `"web "` itself when surfacing the type to
JavaScript. The Chromium reader validates each key via
`net::ParseMimeTypeWithoutParameter`, which splits on `/` and requires
both sides to be valid HTTP tokens. A key like `"web data/my-custom-format"`
fails this validation because `"web data"` contains a space (not a valid
token character), and the entry is silently dropped — `item.types` in
JavaScript would not include the custom type at all.

Chromium's own writer documents this convention explicitly
(`third_party/blink/renderer/modules/clipboard/clipboard_writer.cc`):

> We write the custom MIME type without the "web " prefix into the web
> custom format map so native applications don't have to add any string
> parsing logic to read format from clipboard.

So a web page that writes `new ClipboardItem({"web data/my-custom-format": blob})`
produces the same map key as our app: `"data/my-custom-format"`. The
round-trip produces `"web data/my-custom-format"` on the read side
because Chromium prepends the prefix during
`OnCustomFormatDataRead`.

### Payload contents

`Web Custom Format0` resolves to the JSON array:

```json
[{"row":1,"col":1,"content":"Cell(1,1)"},{"row":1,"col":2,"content":"Cell(1,2)"}, ...]
```

UTF-8 encoded, no null terminator, same rationale.

## Delay Semantics

Only the payload render is gated by the user-configured delay. The map
renders instantly. This is intentional:

- A Chromium consumer pastes → Windows issues two `WM_RENDERFORMAT`
  messages, typically map first then payload.
- If both used the delay, a single paste would take `2 × delay` seconds.
- The map is trivial metadata with a fixed structure — there is nothing
  to "simulate" about its generation.

So the paste experience is: fast map → delayed payload → paste
completes. One delay per paste.

## Cancellation

The delay loop is cancellable via a second Copy click. Map rendering is
instantaneous, so it isn't cancellable — but nothing is lost either,
because a subsequent payload cancellation re-enters `PerformCopy` and
re-promises **both** formats fresh.

## Non-Goals

- **No bare `web data/my-custom-format` format.** We do *not* register
  a Windows clipboard format literally named `web data/my-custom-format`
  alongside the map. Chromium's convention is map-only; apps that would
  expect the bare name aren't part of the Chromium clipboard ecosystem.
  Adding that layer later is additive: register one more format, add
  one more promise, add one more render branch.

- **Single slot.** Chromium reserves slots 0–15. We use only slot 0
  because we publish a single custom MIME type. Multiple custom MIME
  types would each claim a distinct slot index.

## Interoperability

- **Reads from Chromium-based browsers:** pages calling
  `navigator.clipboard.read()` with a custom format hint like
  `"web data/my-custom-format"` should discover our payload via the map.
  (Subject to browser security policies — unsanitized custom formats
  currently require a user gesture and may need feature flags in some
  builds.)

- **Reads from other native apps:** an app that doesn't know about the
  Chromium map convention will not find anything useful, because the
  MIME-like string is a JSON key, not a registered format name. That's
  the whole point of the indirection.

## References

- [W3C Async Clipboard API — Unsanitized Custom Formats](https://w3c.github.io/clipboard-apis/)
- [Chromium design: Web Custom Formats on the native clipboard](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/ui/base/clipboard/)
- See also: [win32-clipboard-apis.md](win32-clipboard-apis.md),
  [delayed-clipboard-rendering.md](delayed-clipboard-rendering.md),
  [implementation-patterns.md](implementation-patterns.md).
