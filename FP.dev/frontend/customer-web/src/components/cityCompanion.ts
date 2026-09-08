import { ref } from 'vue'

export interface VisitedProduct { id: string; name: string }
const key = 'doselect-city-recent-products'
export const recentProducts = ref<VisitedProduct[]>([])
export const companionMessage = ref('')
let messageTimer: ReturnType<typeof setTimeout> | undefined

export function loadRecentProducts() {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(key) ?? '[]')
    recentProducts.value = Array.isArray(value) ? value.filter((item): item is VisitedProduct =>
      typeof item?.id === 'string' && /^[a-z0-9-]{1,80}$/i.test(item.id)
      && typeof item?.name === 'string' && item.name.length <= 200).slice(0, 4) : []
  } catch { recentProducts.value = [] }
}
export function rememberProduct(item: VisitedProduct) {
  recentProducts.value = [item, ...recentProducts.value.filter(entry => entry.id !== item.id)].slice(0, 4)
  save()
}
export function clearRecentProducts() { recentProducts.value = []; save() }
function save() {
  try { localStorage.setItem(key, JSON.stringify(recentProducts.value)) } catch { /* Browsing works without storage. */ }
}
export function celebrateCart() {
  clearTimeout(messageTimer)
  companionMessage.value = '已放進購物車囉！你的新裝備又近了一步。'
  messageTimer = setTimeout(() => { companionMessage.value = '' }, 5000)
}
