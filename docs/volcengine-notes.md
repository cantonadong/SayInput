# Volcengine streaming ASR protocol notes

Verified 2026-09-14 against the current [official streaming API documentation](https://docs.volcengine.com/docs/6561/1354869?lang=zh). Its JavaScript page's public content endpoint is `https://docs.volcengine.com/api/doc/getDocDetail?LibraryID=6561&DocumentID=1354869&lang=zh` (send the documentation page as Referer). Also inspected the [official Python client](https://github.com/volcengine/ai-app-lab/blob/main/arkitect/core/component/asr/asr_client.py) and [binary protocol implementation](https://github.com/volcengine/ai-app-lab/blob/main/arkitect/utils/binary_protocol.py).

## Connection

- Recommended optimized bidirectional endpoint: `wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async`. Responses arrive when results change; do not wait for one response per audio chunk.
- Older bidirectional endpoint: `/api/v3/sauc/bigmodel`. Input-only endpoint: `/api/v3/sauc/bigmodel_nostream`.
- New console credentials: `X-Api-Key`. Old console credentials: `X-Api-App-Key` = App ID and `X-Api-Access-Key` = Access Token. These are alternative credential forms.
- Both forms also use `X-Api-Resource-Id`, `X-Api-Request-Id` = fresh UUID, and `X-Api-Sequence: -1`. Official examples additionally send `X-Api-Connect-Id` = UUID. Capture response `X-Tt-Logid` for diagnosis.
- Resource IDs: `volc.bigasr.sauc.duration` / `volc.bigasr.sauc.concurrent` for 1.0; `volc.seedasr.sauc.duration` / `volc.seedasr.sauc.concurrent` for 2.0. Resource entitlement must match the credential.

## Frames

Four-byte header: byte 0 `0x11` (version 1, four-byte header); byte 1 upper nibble message type, lower nibble flags; byte 2 upper nibble serialization, lower nibble compression; byte 3 zero. Decode payload offset from `(byte0 & 15) * 4`.

Types: 1 full client configuration, 2 audio, 9 full server response, 15 server error. Serialization: 0 raw, 1 JSON. Compression: 0 none, 1 gzip. Flags bit 0 means a signed big-endian int32 sequence follows the header; bit 1 means final packet. Thus final flag 2 has no sequence; final flag 3 has one. Never assume every response includes a sequence.

After header/optional sequence, ordinary frames carry unsigned big-endian uint32 payload length and payload. Length describes compressed bytes; header and size remain uncompressed. Error frames carry uint32 error code, uint32 payload length, then payload. Decode payload according to compression and serialization bits.

Send configuration first (JSON/gzip), then audio chunks (raw/gzip); mark last audio with flag 2, or flag 3 plus negative sequence if using sequence numbers. The official client awaits an initial response before audio and runs sending/receiving concurrently afterward. No separate JSON session-start/session-end event protocol is involved.

## Configuration and results

```json
{
  "user": { "uid": "sayinput" },
  "audio": { "format": "pcm", "codec": "raw", "rate": 16000, "bits": 16, "channel": 1 },
  "request": { "model_name": "bigmodel", "enable_itn": true, "enable_punc": true, "show_utterances": true, "result_type": "full" }
}
```

PCM must be signed 16-bit little-endian; documentation supports 16 kHz only. Recommended chunks are 100–200 ms, with 200 ms recommended for bidirectional recognition (6,400 bytes mono PCM). Explicit `audio.language` is supported only by `bigmodel_nostream`; omit for bidirectional modes, which support Chinese and English by default.

`result.text` contains recognition text. `result_type: full` is cumulative; `single` excludes previous utterances. With `show_utterances`, `result.utterances` includes text, start/end milliseconds and `definite`. `definite` means that utterance is confirmed, not that the whole session ended. The binary final flag ends the session result. Do not append successive cumulative texts.

Optimized bidirectional mode supports `enable_nonstream: true` for a second recognition pass; only the second pass marks utterances definite. This defaults VAD sentence boundaries to 800 ms; `end_window_size` can configure the silence threshold (minimum 200 ms). This feature is unavailable on the older bidirectional endpoint.

The requested [SDK page](https://docs.volcengine.com/docs/6561/1395846?lang=zh) and [product introduction](https://docs.volcengine.com/docs/6561/1354871?lang=zh) resolve through the same public content API. Requested document 1354867 returned no content; the actual streaming wire protocol document is 1354869.
