<script setup lang="ts">
/**
 * C-14 結帳頁：收件資料、配送（含示範門市）、付款方式、模擬發票及政策確認。只送識別與使用者輸入，
 * 不送價格——金額、折扣、運費一律由後端在 `POST /orders` 的同一交易內重新計算與快照
 * （M功能桌面UI與Route規格 C-14）。
 *
 * 配送段（ShippingOptionList／ConvenienceStorePicker）是既有共用元件（PR #79），這裡只負責組裝
 * 收件資料、付款方式、發票與政策這三個屬於這個工作包的部分，並在送出前用一次性的購物車重新驗證
 * （跟 CartPage 同一支 revalidate）確保結帳當下的購物車仍然合法——深連結直接進這頁時，不能信任
 * 「使用者一定是從已經驗證過的購物車頁點過來的」。
 */
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { useRevalidateCart } from '../features/cart/useCart'
import type { CartDto, CartValidationDto } from '../features/cart/types'
import { useShippingOptions } from '../features/shipping/useShipping'
import ShippingOptionList from '../features/shipping/components/ShippingOptionList.vue'
import ConvenienceStorePicker from '../features/shipping/components/ConvenienceStorePicker.vue'
import type { ConvenienceStoreOptionDto } from '../features/shipping/types'
import { useSessionStore } from '../stores/session'
import { useAddressesQuery, useProfileQuery } from '../features/members/queries'
import type { MemberAddress } from '../features/members/api'
import { useCheckoutPolicyVersionsQuery, useSubmitCheckoutMutation } from '../features/checkout/useCheckout'
import type { CheckoutInvoiceBuyerType, PaymentMethod } from '../features/checkout/api'

const router = useRouter()
const sessionStore = useSessionStore()

type PageState = 'loading' | 'error' | 'empty' | 'not-ready' | 'ready'
const pageState = ref<PageState>('loading')
const validation = ref<CartValidationDto>()
const validationError = ref<unknown>()
const cart = computed<CartDto | undefined>(() => validation.value?.cart)

const revalidateMutation = useRevalidateCart()

async function runValidation(): Promise<void> {
  pageState.value = 'loading'
  validationError.value = undefined
  try {
    const result = await revalidateMutation.mutateAsync()
    validation.value = result
    if (result.cart.items.length === 0) {
      pageState.value = 'empty'
    } else if (!result.isCheckoutReady) {
      pageState.value = 'not-ready'
    } else {
      pageState.value = 'ready'
    }
  } catch (error) {
    validationError.value = error
    pageState.value = 'error'
  }
}

// A deep link straight into /checkout can't assume the shopper already validated on /cart —
// re-validate here, once, as soon as cart identity is actually resolved (not before: see
// useShippingOptions/useCart's own identity-confirmation gating for why).
watch(
  () => sessionStore.isIdentityConfirmed,
  (confirmed) => {
    if (confirmed) {
      void runValidation()
    }
  },
  { immediate: true },
)

const shippingQuery = useShippingOptions(
  computed(() => pageState.value === 'ready'),
  computed(() => cart.value?.rowVersion),
)

const selectedMethodCode = ref<string | null>(null)
const selectedOption = computed(
  () => shippingQuery.data.value?.options.find(option => option.methodCode === selectedMethodCode.value) ?? null,
)

const storePublicId = ref<string | null>(null)
const storeSummary = ref<ConvenienceStoreOptionDto | null>(null)

const addressForm = reactive({
  recipientName: '',
  phone: '',
  postalCode: '',
  city: '',
  district: '',
  addressLine1: '',
  addressLine2: '',
})
const isAddressComplete = computed(() =>
  Boolean(addressForm.recipientName && addressForm.phone && addressForm.postalCode
    && addressForm.city && addressForm.district && addressForm.addressLine1))

// Changing shipping method invalidates whatever store/payment choice went with the previous one
// (a different method has its own eligible payment methods and its own store-vs-address need).
watch(selectedMethodCode, () => {
  storePublicId.value = null
  storeSummary.value = null
  paymentMethod.value = null
})

const profileQuery = useProfileQuery(computed(() => sessionStore.isAuthenticated))
const addressesQuery = useAddressesQuery(computed(() => sessionStore.isAuthenticated))

const buyerForm = reactive({ email: '', name: '', phone: '' })
watch(() => profileQuery.data.value, (profile) => {
  if (profile) {
    buyerForm.name ||= profile.displayName
    buyerForm.phone ||= profile.phone ?? ''
  }
})

const selectedSavedAddressId = ref('manual')
function applySavedAddress(address: MemberAddress): void {
  addressForm.recipientName = address.recipientName
  addressForm.phone = address.phone
  addressForm.postalCode = address.postalCode
  addressForm.city = address.city
  addressForm.district = address.district
  addressForm.addressLine1 = address.addressLine1
  addressForm.addressLine2 = address.addressLine2 ?? ''
}
function onSelectSavedAddress(): void {
  const address = addressesQuery.data.value?.find(item => item.publicId === selectedSavedAddressId.value)
  if (address) {
    applySavedAddress(address)
  }
}

const PAYMENT_METHOD_LABELS: Record<PaymentMethod, string> = {
  creditCard: '信用卡',
  atm: 'ATM 虛擬帳號',
  convenienceCode: '超商代碼',
  cashOnDelivery: '貨到付款',
  linePay: 'LINE Pay',
  applePay: 'Apple Pay',
  googlePay: 'Google Pay',
}
const paymentMethod = ref<PaymentMethod | null>(null)
// EfShippingOptionsService.BuildOption 回傳的 allowedPaymentMethods 不是具體付款閘道清單，是粗分類
// ["prepaid", "cashOnDelivery"] 的子集——是否需要預付（COD 資格、組裝／超取限制）才是配送方式在管的
// 事，實際要用信用卡還是 LINE Pay 這類閘道選擇跟配送方式無關。"prepaid" 出現時，所有預付閘道都開放；
// 前台不自行推算 COD 資格本身——那一半的判斷（isEligible／ineligibleReasonCode）仍完全來自後端。
const PREPAID_METHODS: readonly PaymentMethod[] = ['creditCard', 'atm', 'convenienceCode', 'linePay', 'applePay', 'googlePay']
const availablePaymentMethods = computed<PaymentMethod[]>(() => {
  const categories = selectedOption.value?.allowedPaymentMethods ?? []
  const methods: PaymentMethod[] = []
  if (categories.includes('prepaid')) {
    methods.push(...PREPAID_METHODS)
  }
  if (categories.includes('cashOnDelivery')) {
    methods.push('cashOnDelivery')
  }
  return methods
})

const invoiceBuyerType = ref<CheckoutInvoiceBuyerType>('personal')
const carrierType = ref('')
const carrierValue = ref('')
const companyTaxId = ref('')
const companyName = ref('')

const deliveryNote = ref('')
const policyAccepted = ref(false)
const policyQuery = useCheckoutPolicyVersionsQuery()

const submitMutation = useSubmitCheckoutMutation()
const submitError = ref<string>()
// OrdersController's class doc is explicit: order detail/payment always requires either an
// authenticated member or an already-*verified* GuestOrderAccessToken — completing Checkout as a
// guest does not itself grant that (see GuestOrderAccessController, WP-H03). Navigating a guest
// straight to /orders/{id}/payment would just 401. A member's session already satisfies that bar,
// so only they get sent straight there; a guest instead sees an inline confirmation here with a
// link into the real guest-order-access journey.
const completedGuestOrder = ref<{ orderNumber: string } | null>(null)

const canSubmit = computed(() => {
  if (pageState.value !== 'ready' || !cart.value) {
    return false
  }
  if (!selectedOption.value?.isEligible) {
    return false
  }
  if (selectedOption.value.requiresStore && !storePublicId.value) {
    return false
  }
  if (selectedOption.value.requiresAddress && !isAddressComplete.value) {
    return false
  }
  if (!paymentMethod.value) {
    return false
  }
  if (invoiceBuyerType.value === 'company' && (!companyTaxId.value.trim() || !companyName.value.trim())) {
    return false
  }
  if (!buyerForm.email.trim() || !buyerForm.name.trim() || !buyerForm.phone.trim()) {
    return false
  }
  return policyAccepted.value && Boolean(policyQuery.data.value) && !submitMutation.isPending.value
})

async function submit(): Promise<void> {
  if (!canSubmit.value || !cart.value || !selectedOption.value || !paymentMethod.value || !policyQuery.data.value) {
    return
  }
  submitError.value = undefined

  try {
    const order = await submitMutation.mutateAsync({
      idempotencyKey: crypto.randomUUID(),
      body: {
        cartPublicId: cart.value.publicId,
        cartRowVersion: cart.value.rowVersion,
        buyer: {
          email: buyerForm.email.trim(),
          name: buyerForm.name.trim(),
          phone: buyerForm.phone.trim(),
        },
        shipping: {
          methodCode: selectedOption.value.methodCode,
          address: selectedOption.value.requiresAddress
            ? {
                recipientName: addressForm.recipientName.trim(),
                phone: addressForm.phone.trim(),
                postalCode: addressForm.postalCode.trim(),
                city: addressForm.city.trim(),
                district: addressForm.district.trim(),
                addressLine1: addressForm.addressLine1.trim(),
                addressLine2: addressForm.addressLine2.trim() || null,
              }
            : null,
          storePublicId: selectedOption.value.requiresStore ? storePublicId.value : null,
          deliveryNote: deliveryNote.value.trim() || null,
        },
        paymentMethod: paymentMethod.value,
        couponCode: cart.value.coupon?.code ?? null,
        invoice: {
          type: 'simulated',
          buyerType: invoiceBuyerType.value,
          carrierType: invoiceBuyerType.value === 'personal' && carrierType.value.trim() ? carrierType.value.trim() : null,
          carrierValue: invoiceBuyerType.value === 'personal' && carrierValue.value.trim() ? carrierValue.value.trim() : null,
          companyTaxId: invoiceBuyerType.value === 'company' ? companyTaxId.value.trim() : null,
          companyName: invoiceBuyerType.value === 'company' ? companyName.value.trim() : null,
        },
        acceptPolicyVersions: policyQuery.data.value,
      },
    })
    if (sessionStore.isAuthenticated) {
      await router.push({ name: 'order-payment', params: { orderId: order.publicId } })
    } else {
      completedGuestOrder.value = { orderNumber: order.orderNumber }
    }
  } catch (error) {
    submitError.value = describeCheckoutError(error)
  }
}

function describeCheckoutError(error: unknown): string {
  if (!isApiError(error)) {
    return '送出訂單時發生問題，請稍後再試。'
  }
  switch (error.code) {
    case 'concurrency_conflict':
      return '購物車內容已更新，請重新整理這個頁面後再試一次。'
    case 'shipping_method_not_allowed':
    case 'shipping_constraint_exceeded':
      return '所選配送方式目前不適用，請重新選擇。'
    case 'payment_method_not_allowed':
      return '所選付款方式不適用於這個配送方式，請重新選擇。'
    case 'idempotency_payload_conflict':
      return '這次送出的內容與先前不一致，請重新整理這個頁面後再試一次。'
    default:
      return error.message
  }
}

function formatFee(fee: number | string): string {
  return `NT$ ${Number(fee).toLocaleString('zh-Hant-TW')}`
}
</script>

<template>
  <section aria-labelledby="checkout-title">
    <h1 id="checkout-title">
      結帳
    </h1>

    <LoadingState
      v-if="pageState === 'loading'"
      label="購物車確認中"
    />

    <ErrorState
      v-else-if="pageState === 'error'"
      :correlation-id="isApiError(validationError) ? validationError.correlationId : undefined"
      @retry="runValidation"
    />

    <EmptyState
      v-else-if="pageState === 'empty'"
      title="購物車是空的"
      description="請先加入商品後再進行結帳。"
    >
      <RouterLink to="/products">
        瀏覽商品
      </RouterLink>
    </EmptyState>

    <EmptyState
      v-else-if="pageState === 'not-ready'"
      title="購物車尚未通過結帳前檢查"
      description="部分品項需要先處理（缺貨、價格異動或組裝相容性），請回購物車頁確認後再結帳。"
    >
      <RouterLink to="/cart">
        回購物車
      </RouterLink>
    </EmptyState>

    <EmptyState
      v-else-if="completedGuestOrder"
      title="訂單已建立"
      :description="`訂單編號：${completedGuestOrder.orderNumber}。這是訪客訂單，請使用訂單編號與您填寫的 Email 查詢訂單以完成付款。`"
    >
      <RouterLink to="/guest-orders/access">
        查詢訂單並付款
      </RouterLink>
    </EmptyState>

    <form
      v-else-if="cart"
      @submit.prevent="submit"
    >
      <section aria-labelledby="checkout-buyer-title">
        <h2 id="checkout-buyer-title">
          聯絡資料
        </h2>
        <label for="checkout-buyer-name">姓名</label>
        <input
          id="checkout-buyer-name"
          v-model="buyerForm.name"
          required
          maxlength="100"
          autocomplete="name"
        >
        <label for="checkout-buyer-email">Email</label>
        <input
          id="checkout-buyer-email"
          v-model="buyerForm.email"
          type="email"
          required
          maxlength="320"
          autocomplete="email"
        >
        <label for="checkout-buyer-phone">電話</label>
        <input
          id="checkout-buyer-phone"
          v-model="buyerForm.phone"
          required
          maxlength="32"
          autocomplete="tel"
        >
      </section>

      <section aria-labelledby="checkout-shipping-title">
        <h2 id="checkout-shipping-title">
          配送方式
        </h2>
        <LoadingState
          v-if="shippingQuery.isPending.value"
          label="配送方式載入中"
        />
        <ErrorState
          v-else-if="shippingQuery.isError.value"
          @retry="shippingQuery.refetch"
        />
        <ShippingOptionList
          v-else-if="shippingQuery.data.value"
          v-model="selectedMethodCode"
          :options="shippingQuery.data.value.options"
          selectable
        />

        <template v-if="selectedOption?.requiresStore">
          <h3>選擇取貨門市</h3>
          <ConvenienceStorePicker
            v-model="storePublicId"
            v-model:selected-summary="storeSummary"
          />
        </template>

        <template v-else-if="selectedOption?.requiresAddress">
          <h3>收件地址</h3>
          <template v-if="sessionStore.isAuthenticated && addressesQuery.data.value && addressesQuery.data.value.length > 0">
            <label for="checkout-saved-address">使用已存地址</label>
            <select
              id="checkout-saved-address"
              v-model="selectedSavedAddressId"
              @change="onSelectSavedAddress"
            >
              <option value="manual">
                手動輸入
              </option>
              <option
                v-for="address in addressesQuery.data.value"
                :key="address.publicId"
                :value="address.publicId"
              >
                {{ address.label }}（{{ address.recipientName }}）
              </option>
            </select>
          </template>

          <label for="checkout-recipient-name">收件人</label>
          <input
            id="checkout-recipient-name"
            v-model="addressForm.recipientName"
            required
            maxlength="100"
          >
          <label for="checkout-recipient-phone">收件人電話</label>
          <input
            id="checkout-recipient-phone"
            v-model="addressForm.phone"
            required
            maxlength="32"
          >
          <label for="checkout-postal-code">郵遞區號</label>
          <input
            id="checkout-postal-code"
            v-model="addressForm.postalCode"
            required
            maxlength="16"
          >
          <label for="checkout-city">縣市</label>
          <input
            id="checkout-city"
            v-model="addressForm.city"
            required
            maxlength="50"
          >
          <label for="checkout-district">行政區</label>
          <input
            id="checkout-district"
            v-model="addressForm.district"
            required
            maxlength="50"
          >
          <label for="checkout-address-line1">地址</label>
          <input
            id="checkout-address-line1"
            v-model="addressForm.addressLine1"
            required
            maxlength="300"
          >
          <label for="checkout-address-line2">地址第二行（選填）</label>
          <input
            id="checkout-address-line2"
            v-model="addressForm.addressLine2"
            maxlength="300"
          >
        </template>

        <label for="checkout-delivery-note">配送備註（選填）</label>
        <textarea
          id="checkout-delivery-note"
          v-model="deliveryNote"
          maxlength="500"
        />
      </section>

      <section
        v-if="selectedOption"
        aria-labelledby="checkout-payment-title"
      >
        <h2 id="checkout-payment-title">
          付款方式
        </h2>
        <p
          v-if="availablePaymentMethods.length === 0"
          role="alert"
        >
          目前選擇的配送方式沒有可用的付款方式，請改選其他配送方式。
        </p>
        <fieldset v-else>
          <label
            v-for="method in availablePaymentMethods"
            :key="method"
          >
            <input
              v-model="paymentMethod"
              type="radio"
              name="payment-method"
              :value="method"
            >
            {{ PAYMENT_METHOD_LABELS[method] }}
          </label>
        </fieldset>
      </section>

      <section aria-labelledby="checkout-invoice-title">
        <h2 id="checkout-invoice-title">
          模擬發票
        </h2>
        <fieldset>
          <label>
            <input
              v-model="invoiceBuyerType"
              type="radio"
              name="invoice-buyer-type"
              value="personal"
            >
            個人
          </label>
          <label>
            <input
              v-model="invoiceBuyerType"
              type="radio"
              name="invoice-buyer-type"
              value="company"
            >
            公司
          </label>
        </fieldset>

        <template v-if="invoiceBuyerType === 'personal'">
          <label for="checkout-carrier-type">載具類型（選填）</label>
          <input
            id="checkout-carrier-type"
            v-model="carrierType"
            maxlength="30"
            placeholder="例如：mobile"
          >
          <label for="checkout-carrier-value">載具號碼（選填）</label>
          <input
            id="checkout-carrier-value"
            v-model="carrierValue"
            maxlength="64"
          >
        </template>
        <template v-else>
          <label for="checkout-company-tax-id">統一編號</label>
          <input
            id="checkout-company-tax-id"
            v-model="companyTaxId"
            required
            maxlength="8"
          >
          <label for="checkout-company-name">公司抬頭</label>
          <input
            id="checkout-company-name"
            v-model="companyName"
            required
            maxlength="160"
          >
        </template>
      </section>

      <section aria-labelledby="checkout-summary-title">
        <h2 id="checkout-summary-title">
          訂單摘要
        </h2>
        <ul>
          <li
            v-for="item in cart.items"
            :key="item.publicId"
          >
            {{ item.name }} × {{ item.quantity }} — {{ formatFee(item.lineTotal) }}
          </li>
        </ul>
        <p>商品小計：{{ formatFee(cart.amounts.subtotal) }}</p>
        <p v-if="Number(cart.amounts.couponDiscount) > 0">
          優惠折抵：-{{ formatFee(cart.amounts.couponDiscount) }}
        </p>
        <p v-if="selectedOption">
          配送費用：{{ formatFee(selectedOption.fee) }}
        </p>
        <p class="checkout-page__note">
          實際應付金額以送出訂單後、伺服器計算的結果為準。
        </p>
      </section>

      <section aria-labelledby="checkout-policy-title">
        <h2 id="checkout-policy-title">
          政策確認
        </h2>
        <label>
          <input
            v-model="policyAccepted"
            type="checkbox"
            :disabled="!policyQuery.data.value"
          >
          我已閱讀並同意服務條款、退換貨政策與隱私權政策
        </label>
      </section>

      <p
        v-if="submitError"
        role="alert"
      >
        {{ submitError }}
      </p>

      <button
        type="submit"
        :disabled="!canSubmit"
      >
        {{ submitMutation.isPending.value ? '送出中…' : '確認送出訂單' }}
      </button>
    </form>
  </section>
</template>

<style scoped>
.checkout-page__note {
  color: #6b7280;
  font-size: 0.8125rem;
}
</style>
