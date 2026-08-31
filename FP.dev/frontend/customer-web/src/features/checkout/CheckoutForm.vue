<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { useCart } from '../cart/useCart'
import { useSessionStore } from '../../stores/session'
import { CURRENT_CHECKOUT_POLICY_VERSIONS, type CreateOrderRequest, type PaymentMethod } from './api'
import { useCreateOrder, useShippingOptions } from './useCheckout'

const router = useRouter()
const sessionStore = useSessionStore()

const { data: cart, isPending: isCartPending, isError: isCartError, refetch: refetchCart } = useCart()
const shippingOptionsQuery = useShippingOptions()
const createOrderMutation = useCreateOrder()

// One idempotency key per page load: retrying the same submission (e.g. after a transient
// network failure) must reuse it so the backend recognizes the retry as the same attempt instead
// of creating a second order; a genuinely new attempt only happens via a fresh page load.
const idempotencyKey = crypto.randomUUID()

const homeDeliveryOptions = computed(() =>
  (shippingOptionsQuery.data.value ?? []).filter(method => method.kind !== 'ConvenienceStorePickup'))

const form = reactive({
  buyerEmail: '',
  buyerName: '',
  buyerPhone: '',
  shippingMethodCode: '',
  recipientName: '',
  recipientPhone: '',
  postalCode: '',
  city: '',
  district: '',
  addressLine1: '',
  addressLine2: '',
  deliveryNote: '',
  paymentMethod: 'creditCard' as PaymentMethod,
  couponCode: '',
  invoiceBuyerType: 'personal' as 'personal' | 'company',
  companyTaxId: '',
  companyName: '',
  carrierType: '',
  carrierValue: '',
  acceptedPolicies: false,
})

const selectedMethod = computed(() =>
  homeDeliveryOptions.value.find(method => method.code === form.shippingMethodCode))

// The shipping-options query resolves asynchronously (a mounted-only hook would run before it
// does), and a shopper could also lose the previously-selected method if it drops out of the
// list on a refetch — only fill the field in when nothing valid is currently selected.
watch(homeDeliveryOptions, (options) => {
  if (options.some(method => method.code === form.shippingMethodCode)) {
    return
  }
  const first = options[0]
  if (first) {
    form.shippingMethodCode = first.code
  }
}, { immediate: true })

const submitError = ref<string | null>(null)

const canSubmit = computed(() =>
  !createOrderMutation.isPending.value &&
  !!cart.value && cart.value.items.length > 0 &&
  !!form.shippingMethodCode &&
  form.acceptedPolicies)

function buildRequestBody(): CreateOrderRequest | null {
  if (!cart.value) {
    return null
  }

  return {
    cartPublicId: cart.value.publicId,
    cartRowVersion: cart.value.rowVersion,
    buyer: {
      email: form.buyerEmail.trim(),
      name: form.buyerName.trim(),
      phone: form.buyerPhone.trim(),
    },
    shipping: {
      methodCode: form.shippingMethodCode,
      address: {
        recipientName: form.recipientName.trim(),
        phone: form.recipientPhone.trim(),
        postalCode: form.postalCode.trim() || null,
        city: form.city.trim() || null,
        district: form.district.trim() || null,
        addressLine1: form.addressLine1.trim() || null,
        addressLine2: form.addressLine2.trim() || null,
      },
      storePublicId: null,
      deliveryNote: form.deliveryNote.trim() || null,
    },
    paymentMethod: form.paymentMethod,
    couponCode: form.couponCode.trim() || null,
    invoice: {
      type: 'simulated',
      buyerType: form.invoiceBuyerType,
      carrierType: form.carrierType.trim() || null,
      carrierValue: form.carrierValue.trim() || null,
      companyTaxId: form.invoiceBuyerType === 'company' ? form.companyTaxId.trim() || null : null,
      companyName: form.invoiceBuyerType === 'company' ? form.companyName.trim() || null : null,
    },
    acceptPolicyVersions: CURRENT_CHECKOUT_POLICY_VERSIONS,
  }
}

async function submit(): Promise<void> {
  submitError.value = null
  const body = buildRequestBody()
  if (!body) {
    return
  }

  try {
    const order = await createOrderMutation.mutateAsync({
      body,
      idempotencyKey,
      isMember: sessionStore.isAuthenticated,
    })
    await router.push({ name: 'order-detail', params: { orderId: order.publicId } })
  } catch (error) {
    submitError.value = describeSubmitError(error)
  }
}

function describeSubmitError(error: unknown): string {
  if (!isApiError(error)) {
    return '建立訂單時發生未預期的錯誤，請稍後再試一次。'
  }

  switch (error.code) {
    case 'inventory_insufficient':
      return '購物車中有商品庫存不足，請回購物車調整數量。'
    case 'order_total_changed':
      return '商品價格或優惠已變動，請重新確認購物車後再結帳。'
    case 'order_total_below_minimum':
      return '折扣後金額低於可結帳的最低金額。'
    case 'cart_item_requires_attention':
      return '購物車中有商品已下架或無法購買，請回購物車處理。'
    case 'payment_method_not_allowed':
      return '所選付款方式不適用於這筆訂單，請改選其他付款方式。'
    case 'payment_cod_amount_exceeded':
      return '貨到付款金額超過上限，請改選其他付款方式。'
    case 'payment_cod_restricted_item':
      return '購物車中有商品不支援貨到付款，請改選其他付款方式。'
    case 'shipping_method_not_allowed':
      return '所選配送方式目前無法使用，請重新選擇。'
    case 'concurrency_conflict':
      return '購物車已被更新，請重新整理購物車後再試一次。'
    case 'validation_failed':
      return '請確認收件資料與付款資訊是否完整。'
    default:
      return `建立訂單時發生問題（${error.code}），請稍後再試一次。`
  }
}

const paymentMethodOptions: ReadonlyArray<{ value: PaymentMethod, label: string }> = [
  { value: 'creditCard', label: '信用卡' },
  { value: 'atm', label: 'ATM 轉帳' },
  { value: 'convenienceCode', label: '超商代碼' },
  { value: 'cashOnDelivery', label: '貨到付款' },
  { value: 'linePay', label: 'LINE Pay' },
  { value: 'applePay', label: 'Apple Pay' },
  { value: 'googlePay', label: 'Google Pay' },
]

function formatTwd(amount: number | string): string {
  return `NT$${Number(amount).toLocaleString('zh-Hant-TW')}`
}
</script>

<template>
  <LoadingState
    v-if="isCartPending"
    label="購物車載入中"
  />

  <ErrorState
    v-else-if="isCartError"
    @retry="refetchCart"
  />

  <EmptyState
    v-else-if="!cart || cart.items.length === 0"
    title="購物車是空的"
    description="請先將商品加入購物車再進行結帳。"
  >
    <RouterLink to="/cart">
      回購物車
    </RouterLink>
  </EmptyState>

  <form
    v-else
    class="checkout-form"
    novalidate
    @submit.prevent="submit"
  >
    <section aria-labelledby="checkout-summary-title">
      <h2 id="checkout-summary-title">
        訂單摘要
      </h2>
      <ul class="checkout-form__summary-items">
        <li
          v-for="item in cart.items"
          :key="item.publicId"
        >
          {{ item.name }} × {{ item.quantity }}
        </li>
      </ul>
      <p class="checkout-form__summary-total">
        商品小計：{{ formatTwd(cart.amounts.totalEstimate) }}（實際應付金額將於送出後以系統重新計算為準）
      </p>
    </section>

    <section aria-labelledby="checkout-buyer-title">
      <h2 id="checkout-buyer-title">
        聯絡資訊
      </h2>
      <div class="form-field">
        <label for="checkout-buyer-email">Email</label>
        <input
          id="checkout-buyer-email"
          v-model="form.buyerEmail"
          type="email"
          autocomplete="email"
          required
        >
      </div>
      <div class="form-field">
        <label for="checkout-buyer-name">姓名</label>
        <input
          id="checkout-buyer-name"
          v-model="form.buyerName"
          type="text"
          autocomplete="name"
          required
        >
      </div>
      <div class="form-field">
        <label for="checkout-buyer-phone">手機號碼</label>
        <input
          id="checkout-buyer-phone"
          v-model="form.buyerPhone"
          type="tel"
          autocomplete="tel"
          required
        >
      </div>
    </section>

    <section aria-labelledby="checkout-shipping-title">
      <h2 id="checkout-shipping-title">
        配送方式
      </h2>

      <LoadingState
        v-if="shippingOptionsQuery.isPending.value"
        label="配送方式載入中"
      />
      <ErrorState
        v-else-if="shippingOptionsQuery.isError.value"
        @retry="shippingOptionsQuery.refetch"
      />
      <template v-else>
        <div class="form-field">
          <label for="checkout-shipping-method">配送方式</label>
          <select
            id="checkout-shipping-method"
            v-model="form.shippingMethodCode"
            required
          >
            <option
              value=""
              disabled
            >
              請選擇配送方式
            </option>
            <option
              v-for="method in homeDeliveryOptions"
              :key="method.code"
              :value="method.code"
            >
              {{ method.nameZhTw }}（運費 {{ formatTwd(method.baseFee) }}<template v-if="method.freeShippingThreshold != null">
                ，滿 {{ formatTwd(method.freeShippingThreshold) }} 免運
              </template>）
            </option>
          </select>
          <p
            v-if="homeDeliveryOptions.length === 0"
            class="form-field__hint"
          >
            目前沒有可用的配送方式，暫時無法結帳。
          </p>
          <p
            v-if="selectedMethod && !selectedMethod.allowsCod"
            class="form-field__hint"
          >
            此配送方式不支援貨到付款。
          </p>
        </div>

        <div class="form-field">
          <label for="checkout-recipient-name">收件人姓名</label>
          <input
            id="checkout-recipient-name"
            v-model="form.recipientName"
            type="text"
            required
          >
        </div>
        <div class="form-field">
          <label for="checkout-recipient-phone">收件人電話</label>
          <input
            id="checkout-recipient-phone"
            v-model="form.recipientPhone"
            type="tel"
            required
          >
        </div>
        <div class="form-field">
          <label for="checkout-postal-code">郵遞區號</label>
          <input
            id="checkout-postal-code"
            v-model="form.postalCode"
            type="text"
          >
        </div>
        <div class="form-field">
          <label for="checkout-city">縣市</label>
          <input
            id="checkout-city"
            v-model="form.city"
            type="text"
          >
        </div>
        <div class="form-field">
          <label for="checkout-district">鄉鎮市區</label>
          <input
            id="checkout-district"
            v-model="form.district"
            type="text"
          >
        </div>
        <div class="form-field">
          <label for="checkout-address-line1">地址</label>
          <input
            id="checkout-address-line1"
            v-model="form.addressLine1"
            type="text"
          >
        </div>
        <div class="form-field">
          <label for="checkout-address-line2">地址第二行（選填）</label>
          <input
            id="checkout-address-line2"
            v-model="form.addressLine2"
            type="text"
          >
        </div>
        <div class="form-field">
          <label for="checkout-delivery-note">配送備註（選填）</label>
          <textarea
            id="checkout-delivery-note"
            v-model="form.deliveryNote"
            maxlength="500"
          />
        </div>
      </template>
    </section>

    <section aria-labelledby="checkout-payment-title">
      <h2 id="checkout-payment-title">
        付款方式
      </h2>
      <div class="form-field">
        <label for="checkout-payment-method">付款方式</label>
        <select
          id="checkout-payment-method"
          v-model="form.paymentMethod"
        >
          <option
            v-for="option in paymentMethodOptions"
            :key="option.value"
            :value="option.value"
          >
            {{ option.label }}
          </option>
        </select>
      </div>
      <div class="form-field">
        <label for="checkout-coupon-code">優惠券代碼（選填）</label>
        <input
          id="checkout-coupon-code"
          v-model="form.couponCode"
          type="text"
        >
      </div>
    </section>

    <section aria-labelledby="checkout-invoice-title">
      <h2 id="checkout-invoice-title">
        電子發票（模擬）
      </h2>
      <div class="form-field">
        <label for="checkout-invoice-buyer-type">發票類型</label>
        <select
          id="checkout-invoice-buyer-type"
          v-model="form.invoiceBuyerType"
        >
          <option value="personal">
            個人
          </option>
          <option value="company">
            公司
          </option>
        </select>
      </div>
      <template v-if="form.invoiceBuyerType === 'company'">
        <div class="form-field">
          <label for="checkout-company-tax-id">統一編號</label>
          <input
            id="checkout-company-tax-id"
            v-model="form.companyTaxId"
            type="text"
            maxlength="8"
            required
          >
        </div>
        <div class="form-field">
          <label for="checkout-company-name">公司抬頭</label>
          <input
            id="checkout-company-name"
            v-model="form.companyName"
            type="text"
            required
          >
        </div>
      </template>
      <template v-else>
        <div class="form-field">
          <label for="checkout-carrier-value">手機條碼載具（選填）</label>
          <input
            id="checkout-carrier-value"
            v-model="form.carrierValue"
            type="text"
            @input="form.carrierType = form.carrierValue ? 'mobile' : ''"
          >
        </div>
      </template>
    </section>

    <section aria-labelledby="checkout-policy-title">
      <h2 id="checkout-policy-title">
        確認條款
      </h2>
      <label class="checkout-form__policy-checkbox">
        <input
          v-model="form.acceptedPolicies"
          type="checkbox"
        >
        我已閱讀並同意服務條款、退貨政策與隱私權政策
      </label>
    </section>

    <p
      v-if="submitError"
      class="checkout-form__error"
      role="alert"
    >
      {{ submitError }}
    </p>

    <button
      type="submit"
      :disabled="!canSubmit"
    >
      {{ createOrderMutation.isPending.value ? '送出中…' : '送出訂單' }}
    </button>
  </form>
</template>

<style scoped>
.checkout-form {
  display: flex;
  flex-direction: column;
  gap: 2rem;
  max-width: 40rem;
}

.checkout-form__summary-items {
  margin: 0 0 0.5rem;
  padding-left: 1.25rem;
}

.checkout-form__summary-total {
  font-weight: 600;
}

.checkout-form__policy-checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.checkout-form__error {
  color: #b91c1c;
}
</style>
