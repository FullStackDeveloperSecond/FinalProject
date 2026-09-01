<script setup lang="ts">
/**
 * 購物車與結帳共用的配送方式清單（C-13 的「Shipping Options」、C-14 的「配送」段）。
 *
 * 不可選的原因**照實顯示**而不是把選項藏起來：購物車、訂單、付款與物流.md 要求「只能選擇宅配並
 * 顯示原因」，顧客要看得到「為什麼超取不能用」才知道要拆單還是改配送。可選與否一律以後端的
 * `isEligible` 為準——尺寸／重量／組裝的判定在後端，前台不自行推算。
 */
import { computed } from 'vue'
import type { ShippingOptionDto } from '../types'

const props = defineProps<{
  options: ShippingOptionDto[]
  /** 目前選取的配送方式代碼；唯讀展示（購物車頁）時不傳。 */
  modelValue?: string | null
  /** 購物車頁只是預覽費用，不讓顧客在這裡做選擇。 */
  selectable?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [string] }>()

const INELIGIBLE_REASONS: Record<string, string> = {
  // API錯誤碼目錄的物流碼；後端在選項上以 ineligibleReasonCode 回同一組字串。
  shipping_method_not_allowed: '此配送方式不適用於購物車中的商品（尺寸、重量、分類或組裝限制）',
  shipping_constraint_exceeded: '超過這個配送方式的包裹尺寸或重量限制',
}

function describeReason(code: string | null | undefined): string {
  if (!code) {
    return '目前無法選擇這個配送方式'
  }
  return INELIGIBLE_REASONS[code] ?? '目前無法選擇這個配送方式'
}

function formatFee(fee: number | string): string {
  return `NT$ ${Number(fee).toLocaleString('zh-Hant-TW')}`
}

const hasEligibleOption = computed(() => props.options.some((option) => option.isEligible))

function select(option: ShippingOptionDto) {
  if (!props.selectable || !option.isEligible) {
    return
  }
  emit('update:modelValue', option.methodCode)
}
</script>

<template>
  <div class="shipping-options">
    <p
      v-if="options.length > 0 && !hasEligibleOption"
      class="shipping-options__none"
      role="alert"
    >
      目前購物車沒有可用的配送方式，請調整商品後再試。
    </p>

    <ul
      class="shipping-options__list"
      :aria-label="selectable ? '選擇配送方式' : '可用配送方式'"
    >
      <li
        v-for="option in options"
        :key="option.methodCode"
        class="shipping-options__item"
        :class="{ 'shipping-options__item--ineligible': !option.isEligible }"
      >
        <label class="shipping-options__label">
          <input
            v-if="selectable"
            type="radio"
            name="shipping-method"
            :value="option.methodCode"
            :checked="modelValue === option.methodCode"
            :disabled="!option.isEligible"
            :aria-label="option.name"
            @change="select(option)"
          >
          <span class="shipping-options__name">{{ option.name }}</span>
          <span class="shipping-options__fee">{{ formatFee(option.fee) }}</span>
        </label>

        <p
          v-if="option.isEligible && option.freeShippingThreshold !== null && option.freeShippingThreshold !== undefined"
          class="shipping-options__hint"
        >
          消費滿 {{ formatFee(option.freeShippingThreshold) }} 免運
        </p>
        <p
          v-if="!option.isEligible"
          class="shipping-options__reason"
        >
          {{ describeReason(option.ineligibleReasonCode) }}
        </p>
        <p
          v-else-if="selectable && option.requiresStore"
          class="shipping-options__hint"
        >
          需選擇取貨門市
        </p>
        <p
          v-else-if="selectable && option.requiresAddress"
          class="shipping-options__hint"
        >
          需填寫收件地址
        </p>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.shipping-options__list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.shipping-options__item {
  padding: 0.75rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
}

.shipping-options__item--ineligible {
  background: #f9fafb;
  color: #6b7280;
}

.shipping-options__label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.shipping-options__name {
  flex: 1;
}

.shipping-options__fee {
  font-variant-numeric: tabular-nums;
}

.shipping-options__hint,
.shipping-options__reason {
  margin: 0.25rem 0 0;
  font-size: 0.8125rem;
}

.shipping-options__reason {
  color: #b91c1c;
}

.shipping-options__hint {
  color: #6b7280;
}

.shipping-options__none {
  color: #b91c1c;
  margin: 0 0 0.75rem;
}
</style>
