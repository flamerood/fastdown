const CHUNK = 128 * 1024;
const sane = (width, height) => width >= 16 && height >= 16 && width <= 16384 && height <= 16384;
const text = (bytes, offset, count) => String.fromCharCode(...bytes.subarray(offset, offset + count));

export function mp4Dimensions(bytes, baseOffset = 0) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let dimensions = null, nextOffset = null, visited = 0;
  function boxes(start, end, depth = 0) {
    if (depth > 8) return;
    for (let offset = start; offset + 8 <= end && ++visited < 5000;) {
      let size = view.getUint32(offset), header = 8;
      const type = text(bytes, offset + 4, 4);
      if (size === 1) { if (offset + 16 > end) break; const large = view.getBigUint64(offset + 8); if (large > BigInt(Number.MAX_SAFE_INTEGER)) break; size = Number(large); header = 16; }
      if (size === 0) size = end - offset;
      if (size < header || !Number.isSafeInteger(size + offset)) break;
      const finish = Math.min(end, offset + size);
      if (type === "tkhd" && offset + size <= end && size >= 84) {
        const width = view.getUint32(offset + size - 8) >>> 16, height = view.getUint32(offset + size - 4) >>> 16;
        if (sane(width, height) && (!dimensions || width * height > dimensions.width * dimensions.height)) dimensions = { width, height };
      }
      if (["moov", "trak", "mdia", "minf", "stbl"].includes(type)) boxes(offset + header, finish, depth + 1);
      if (depth === 0 && offset + size > end && type === "mdat") nextOffset = baseOffset + offset + size;
      if (offset + size > end) break;
      offset += size;
    }
  }
  boxes(0, bytes.length);
  return { dimensions, nextOffset };
}

export function webmDimensions(bytes) {
  let count = 0, dimensions = null;
  function vint(offset, keepMarker = false) {
    if (offset >= bytes.length || bytes[offset] === 0) return null;
    let length = 1; while (length <= 8 && !(bytes[offset] & (1 << (8 - length)))) length++;
    if (length > 8 || offset + length > bytes.length) return null;
    let value = keepMarker ? bytes[offset] : bytes[offset] & ((1 << (8 - length)) - 1);
    let unknown = !keepMarker && value === (1 << (8 - length)) - 1;
    for (let i = 1; i < length; i++) { value = value * 256 + bytes[offset + i]; unknown &&= bytes[offset + i] === 255; }
    return { value, length, unknown };
  }
  function walk(start, end, depth = 0) {
    if (depth > 7) return;
    let width = 0, height = 0;
    for (let offset = start; offset < end && ++count < 5000;) {
      const id = vint(offset, true); if (!id) break;
      const size = vint(offset + id.length); if (!size) break;
      const body = offset + id.length + size.length, finish = size.unknown ? end : Math.min(end, body + size.value);
      if ([0x18538067, 0x1654ae6b, 0xae, 0xe0].includes(id.value)) walk(body, finish, depth + 1);
      if ([0xb0, 0xba].includes(id.value) && size.value <= 4 && body + size.value <= end) {
        let value = 0; for (let i = body; i < finish; i++) value = value * 256 + bytes[i];
        if (id.value === 0xb0) width = value; else height = value;
      }
      if (finish <= offset) break;
      offset = finish;
    }
    if (sane(width, height)) dimensions = { width, height };
  }
  walk(0, bytes.length); return dimensions;
}

export async function boundedResponse(response, limit, signal) {
  const reader = response.body?.getReader(); if (!reader) return new Uint8Array();
  const chunks = []; let length = 0;
  try {
    while (length < limit && !signal?.aborted) {
      const { value, done } = await reader.read(); if (done) break;
      const part = value.subarray(0, limit - length); chunks.push(part); length += part.length;
    }
  } finally { reader.cancel().catch(() => {}); }
  const result = new Uint8Array(length); let offset = 0;
  for (const chunk of chunks) { result.set(chunk, offset); offset += chunk.length; }
  return result;
}

export async function probeVideo(url, fetcher = fetch) {
  const abort = new AbortController(), timer = setTimeout(() => abort.abort(), 6500);
  try {
    let offset = 0;
    for (let attempt = 0; attempt < 3; attempt++) {
      const response = await fetcher(url, { headers: { Range: `bytes=${offset}-${offset + CHUNK - 1}` }, credentials: "omit", signal: abort.signal });
      if (!response.ok) return null;
      const range = response.headers.get("content-range")?.match(/^bytes (\d+)-(\d+)\/(\d+)$/);
      if (offset && (response.status !== 206 || !range || Number(range[1]) !== offset)) { response.body?.cancel().catch(() => {}); return null; }
      const bytes = await boundedResponse(response, CHUNK, abort.signal);
      if (offset === 0 && bytes[0] === 0x1a && bytes[1] === 0x45 && bytes[2] === 0xdf && bytes[3] === 0xa3) return webmDimensions(bytes);
      const result = mp4Dimensions(bytes, offset);
      if (result.dimensions) return result.dimensions;
      const total = range ? Number(range[3]) : Number(response.headers.get("content-length"));
      if (!result.nextOffset || result.nextOffset <= offset || (total && result.nextOffset >= total)) return null;
      offset = result.nextOffset;
    }
  } catch { }
  finally { clearTimeout(timer); }
  return null;
}
