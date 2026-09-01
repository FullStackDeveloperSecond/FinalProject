/**
 * `<input type="datetime-local">` 的值與 UTC instant 之間的轉換。
 *
 * `datetime-local` 拿到的是**沒有時區的本機牆上時間**，而 JS 也把
 * `new Date('2026-09-01T00:00')` 這種不帶時區的字串當成本機時間解析 ——
 * 所以送出方向（本機字串 → `toISOString()`）本來就是對的。
 *
 * 錯的是載入方向：把 UTC 字串的數字直接切下來塞進輸入框，等於宣稱
 * 「UTC 的 00:00 就是本機的 00:00」。在台灣時區，`2026-09-01T00:00:00Z`
 * 會被讀成本機 `00:00`、再送成 `2026-08-31T16:00:00Z` —— 管理員只改個名稱，
 * 整段有效期就提前八小時。
 */
export function toLocalInputValue(utcIso: string): string {
  const instant = new Date(utcIso)
  // getTimezoneOffset 是「該時刻」的偏移量，所以跨日光節約時間也是對的。
  const localMs = instant.getTime() - instant.getTimezoneOffset() * 60_000
  return new Date(localMs).toISOString().slice(0, 16)
}

/**
 * 表單值轉回 UTC instant。
 *
 * `inputValue` 與 `originalUtc` 的本機顯示值相同時 —— 也就是管理員根本沒動這個
 * 欄位 —— **原樣送回 `originalUtc`**。`datetime-local` 只到分鐘，往返一趟會把
 * 原值的秒與毫秒抹掉；只改名稱不該讓時段因為經過表單而被改寫。
 *
 * 建立新券時沒有原值，直接轉換。
 */
export function toUtcInstant(inputValue: string, originalUtc?: string): string {
  if (originalUtc !== undefined && inputValue === toLocalInputValue(originalUtc)) {
    return originalUtc
  }

  return new Date(inputValue).toISOString()
}
