use crate::error::{AppError, AppResult};
use flate2::{Compression, read::GzDecoder, write::GzEncoder};
use std::io::{Read, Write};
pub const MAX_PAYLOAD: usize = 1024 * 1024;
pub const MAX_FRAME: usize = MAX_PAYLOAD + 256;
pub struct SeedResponse {
    pub text: String,
    pub is_final: bool,
    pub error_code: Option<u32>,
}
pub fn encode_configuration() -> AppResult<Vec<u8>> {
    let json = serde_json::json!({"user":{"uid":"sayinput"},"audio":{"format":"pcm","codec":"raw","rate":16000,"bits":16,"channel":1},"request":{"model_name":"bigmodel","enable_itn":true,"enable_punc":true,"show_utterances":true,"result_type":"full"}});
    encode(
        &serde_json::to_vec(&json).map_err(|_| AppError::Protocol)?,
        1,
        1,
        false,
        None,
    )
}
pub fn encode_audio(pcm: &[u8], final_frame: bool, sequence: Option<i32>) -> AppResult<Vec<u8>> {
    if !pcm.len().is_multiple_of(2)
        || sequence.is_some_and(|s| if final_frame { s >= 0 } else { s <= 0 })
    {
        return Err(AppError::Protocol);
    }
    encode(pcm, 2, 0, final_frame, sequence)
}
fn encode(
    payload: &[u8],
    kind: u8,
    serialization: u8,
    final_frame: bool,
    sequence: Option<i32>,
) -> AppResult<Vec<u8>> {
    if payload.len() > MAX_PAYLOAD {
        return Err(AppError::Protocol);
    }
    let mut gzip = GzEncoder::new(Vec::new(), Compression::fast());
    gzip.write_all(payload).map_err(|_| AppError::Protocol)?;
    let compressed = gzip.finish().map_err(|_| AppError::Protocol)?;
    let mut result = vec![
        0x11,
        (kind << 4) | if final_frame { 2 } else { 0 } | u8::from(sequence.is_some()),
        (serialization << 4) | 1,
        0,
    ];
    if let Some(seq) = sequence {
        result.extend(seq.to_be_bytes());
    }
    result.extend((compressed.len() as u32).to_be_bytes());
    result.extend(compressed);
    Ok(result)
}
pub fn decode(frame: &[u8]) -> AppResult<SeedResponse> {
    if frame.len() < 8 || frame.len() > MAX_FRAME || frame[0] >> 4 != 1 {
        return Err(AppError::Protocol);
    }
    let mut offset = ((frame[0] & 15) * 4) as usize;
    let (kind, flags, serialization, compression) =
        (frame[1] >> 4, frame[1] & 15, frame[2] >> 4, frame[2] & 15);
    if offset < 4
        || offset > frame.len() - 4
        || ![9, 15].contains(&kind)
        || flags > 3
        || serialization > 1
        || compression > 1
    {
        return Err(AppError::Protocol);
    }
    let _sequence = if flags & 1 != 0 {
        Some(number(frame, &mut offset)? as i32)
    } else {
        None
    };
    let error_code = if kind == 15 {
        Some(number(frame, &mut offset)?)
    } else {
        None
    };
    let size = number(frame, &mut offset)? as usize;
    if size > MAX_PAYLOAD || size != frame.len() - offset {
        return Err(AppError::Protocol);
    }
    let payload = if compression == 1 {
        let mut out = Vec::new();
        GzDecoder::new(&frame[offset..])
            .take((MAX_PAYLOAD + 1) as u64)
            .read_to_end(&mut out)
            .map_err(|_| AppError::Protocol)?;
        if out.len() > MAX_PAYLOAD {
            return Err(AppError::Protocol);
        }
        out
    } else {
        frame[offset..].to_vec()
    };
    if error_code.is_some() {
        return Ok(SeedResponse {
            text: String::new(),
            is_final: false,
            error_code,
        });
    }
    if serialization != 1 {
        return Err(AppError::Protocol);
    }
    let value: serde_json::Value =
        serde_json::from_slice(&payload).map_err(|_| AppError::Protocol)?;
    let text = match value.get("result").and_then(|r| r.get("text")) {
        None | Some(serde_json::Value::Null) => String::new(),
        Some(serde_json::Value::String(s)) => s.clone(),
        _ => return Err(AppError::Protocol),
    };
    Ok(SeedResponse {
        text,
        is_final: flags & 2 != 0,
        error_code,
    })
}
fn number(frame: &[u8], offset: &mut usize) -> AppResult<u32> {
    let bytes = frame.get(*offset..*offset + 4).ok_or(AppError::Protocol)?;
    *offset += 4;
    Ok(u32::from_be_bytes(
        bytes.try_into().map_err(|_| AppError::Protocol)?,
    ))
}
