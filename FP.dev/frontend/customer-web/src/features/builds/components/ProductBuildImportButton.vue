<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { getProductDetail } from '../../catalog/api'
import { stageBuildImport } from '../buildImport'
import { BUILD_CATEGORY_SLOTS } from '../types'

const props = defineProps<{ productPublicId: string }>()
const router = useRouter()
const product = ref<Awaited<ReturnType<typeof getProductDetail>> | null>(null)
const selected = ref('')
const busy = ref(false)
const message = ref('')
async function open(): Promise<void> {
  const id = props.productPublicId
  busy.value = true
  message.value = ''
  try {
    const detail = await getProductDetail(id)
    if (id !== props.productPublicId) return
    if (!BUILD_CATEGORY_SLOTS.some(slot => slot.code === detail.category.code)) {
      message.value = '此商品不屬於組裝零件分類。'
      return
    }
    product.value = detail
    selected.value = ''
  } catch { message.value = '無法讀取商品規格，請稍後再試。' }
  finally { busy.value = false }
}
async function confirm(): Promise<void> {
  const detail = product.value
  const sku = detail?.skus.find(item => item.publicId === selected.value)
  if (!detail || !sku || detail.productPublicId !== props.productPublicId) return
  try {
    stageBuildImport({ name: '我的組裝清單', items: [{ skuPublicId: sku.publicId,
      name: sku.name, categoryCode: detail.category.code, quantity: 1 }] })
    await router.push('/builds/new')
  } catch { message.value = '無法暫存匯入內容，請確認瀏覽器允許儲存資料。' }
}
</script>

<template>
  <div>
    <button
      type="button"
      :disabled="busy"
      @click="open"
    >
      挑選規格加入組裝清單
    </button>
    <div v-if="product && product.productPublicId === productPublicId">
      <label>組裝規格（必選）
        <select v-model="selected">
          <option value="">請選擇規格</option>
          <option
            v-for="sku in product.skus"
            :key="sku.publicId"
            :value="sku.publicId"
          >{{ sku.name }}（{{ sku.skuCode }}）</option>
        </select>
      </label>
      <button
        type="button"
        :disabled="!selected"
        @click="confirm"
      >
        預覽匯入
      </button>
      <button
        type="button"
        @click="product = null"
      >
        取消
      </button>
    </div>
    <p
      v-if="message"
      role="alert"
    >
      {{ message }}
    </p>
  </div>
</template>
