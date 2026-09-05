<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { createCorrelationId, isApiError } from '@doselect/web-shared/api'
import { useQueryClient } from '@tanstack/vue-query'
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useSessionStore } from '../stores/session'
import { useCartIdentityKey, useRevalidateCart } from '../features/cart/useCart'
import { clearGuestCartKey, getOrCreateGuestCartKey } from '../features/cart/guestCartKey'
import type { CartDto, CartIssueDto, CartValidationDto } from '../features/cart/types'
import {
  createOrder,
  getCheckoutPolicyVersions,
  type AcceptedPolicyVersions,
  type CreateOrderRequest,
  type OrderDto,
  type PaymentMethod,
} from '../features/checkout/api'
import ShippingOptionList from '../features/shipping/components/ShippingOptionList.vue'
import ConvenienceStorePicker from '../features/shipping/components/ConvenienceStorePicker.vue'
import { useShippingOptions } from '../features/shipping/useShipping'
import type { ConvenienceStoreOptionDto, ShippingOptionDto } from '../features/shipping/types'

interface CheckoutForm {
  buyerEmail: string
  buyerName: string
  buyerPhone: string
  recipientName: string
  recipientPhone: string
  postalCode: string
  city: string
  district: string
  addressLine1: string
  addressLine2: string
  deliveryNote: string
  couponCode: string
  invoiceBuyerType: 'personal' | 'company'
  useMobileBarcode: boolean
  carrierValue: string
  companyTaxId: string
  companyName: string
  acceptTerms: boolean
  acceptReturn: boolean
  acceptPrivacy: boolean
}

type MemberOrderRouteName = 'order-detail' | 'order-payment'

type CreatedOrderHandoff =
  | { kind: 'guest', order: OrderDto }
  | {
      kind: 'member'
      order: OrderDto
      routeName: MemberOrderRouteName
      navigationFailed: boolean
    }

const PAYMENT_LABELS: Record<PaymentMethod, string> = {
  creditCard: '信用卡',
  atm: 'ATM 虛擬帳號',
  convenienceCode: '超商繳費代碼',
  cashOnDelivery: '貨到付款',
  linePay: 'LINE Pay',
  applePay: 'Apple Pay',
  googlePay: 'Google Pay',
}

const ISSUE_MESSAGES: Record<string, string> = {
  sku_unavailable: '購物車中有商品已下架，請回購物車移除。',
  cart_item_requires_attention: '購物車中有商品庫存不足，請回購物車調整。',
  cart_item_limit_exceeded: '購物車數量超過限制，請回購物車調整。',
  cart_merge_conflict: '購物車仍有登入合併衝突，請先處理。',
}

const router = useRouter()
const queryClient = useQueryClient()
const cartIdentityKey = useCartIdentityKey()
const identityKey = computed(() => cartIdentityKey.value.join(' '))
const sessionStore = useSessionStore()
const revalidateCart = useRevalidateCart()

const validation = ref<CartValidationDto | null>(null)
const policyVersions = ref<AcceptedPolicyVersions | null>(null)
const isInitialLoading = ref(false)
const initialError = ref<unknown>(null)
const createdOrderHandoff = ref<CreatedOrderHandoff | null>(null)
let loadGeneration = 0

const selectedShippingMethod = ref<string | null>(null)
const selectedPaymentMethod = ref<PaymentMethod | null>(null)
const selectedStorePublicId = ref<string | null>(null)
const selectedStoreSummary = ref<ConvenienceStoreOptionDto | null>(null)
const appliedCouponCode = ref<string | null>(null)
/** 目前的優惠碼 state 屬於哪個身分；不符就整組作廢，見 syncCouponStateToIdentity。 */
const couponIdentityKey = ref<string | null>(null)
/** 從購物車頁帶過來、還沒被採用的代碼；跨初始化重試保留，見 loadCheckout。 */
const pendingCouponHandoff = ref<{ rowVersion: string, code: string } | null>(null)
/**
 * 真正生效的優惠碼：身分鍵一變就立刻是 null，不等任何 watcher。
 *
 * 直接用 appliedCouponCode 會有一個窗口：session 改變時 useShippingOptions 內部的
 * query key 先反應（它比頁面底下那個 watch 早建立），於是舊代碼會以新身分再送一次
 * 運費查詢。把「屬於哪個身分」寫進讀取端，這個窗口就不存在了。建單請求也讀這一份。
 */
const activeCouponCode = computed(() =>
  couponIdentityKey.value === identityKey.value ? appliedCouponCode.value : null)
const isSubmitting = ref(false)
const submitError = ref<string | null>(null)
const submitCorrelationId = ref<string | null>(null)
const idempotencyState = ref<{ signature: string, key: string } | null>(null)

const form = reactive<CheckoutForm>({
  buyerEmail: '',
  buyerName: '',
  buyerPhone: '',
  recipientName: '',
  recipientPhone: '',
  postalCode: '',
  city: '',
  district: '',
  addressLine1: '',
  addressLine2: '',
  deliveryNote: '',
  couponCode: '',
  invoiceBuyerType: 'personal',
  useMobileBarcode: false,
  carrierValue: '',
  companyTaxId: '',
  companyName: '',
  acceptTerms: false,
  acceptReturn: false,
  acceptPrivacy: false,
})

const cart = computed(() => validation.value?.cart ?? null)
const hasItems = computed(() => (cart.value?.items.length ?? 0) > 0)
const canLoadShipping = computed(() =>
  Boolean(validation.value?.isCheckoutReady && hasItems.value && policyVersions.value),
)

const {
  data: shippingOptions,
  isPending: isShippingPending,
  isError: isShippingError,
  error: shippingError,
  refetch: refetchShipping,
} = useShippingOptions(
  canLoadShipping,
  computed(() => cart.value?.rowVersion),
  activeCouponCode,
)

const selectedShippingOption = computed<ShippingOptionDto | null>(() =>
  shippingOptions.value?.options.find(
    option => option.methodCode === selectedShippingMethod.value,
  ) ?? null,
)

const allowedPaymentMethods = computed<PaymentMethod[]>(() =>
  selectedShippingOption.value?.allowedPaymentMethods ?? [],
)

const isAddressComplete = computed(() => Boolean(
  form.recipientName.trim()
  && form.recipientPhone.trim().length >= 6
  && form.postalCode.trim()
  && form.city.trim()
  && form.district.trim()
  && form.addressLine1.trim(),
))

const isInvoiceComplete = computed(() => {
  if (form.invoiceBuyerType === 'company') {
    return /^\d{8}$/.test(form.companyTaxId.trim()) && Boolean(form.companyName.trim())
  }
  return !form.useMobileBarcode || Boolean(form.carrierValue.trim())
})

const canSubmit = computed(() => {
  const option = selectedShippingOption.value
  if (!cart.value || !policyVersions.value || !validation.value?.isCheckoutReady || !option?.isEligible) {
    return false
  }
  if (option.requiresAddress === option.requiresStore) {
    return false
  }
  if (!form.buyerEmail.includes('@') || !form.buyerName.trim() || form.buyerPhone.trim().length < 6) {
    return false
  }
  if (option.requiresAddress && !isAddressComplete.value) {
    return false
  }
  if (option.requiresStore && !selectedStorePublicId.value) {
    return false
  }
  return Boolean(
    selectedPaymentMethod.value
    && allowedPaymentMethods.value.includes(selectedPaymentMethod.value)
    && isInvoiceComplete.value
    && form.acceptTerms
    && form.acceptReturn
    && form.acceptPrivacy,
  )
})

watch(selectedShippingMethod, () => {
  selectedPaymentMethod.value = null
  selectedStorePublicId.value = null
  selectedStoreSummary.value = null
  submitError.value = null
})

watch(allowedPaymentMethods, (methods) => {
  if (selectedPaymentMethod.value && !methods.includes(selectedPaymentMethod.value)) {
    selectedPaymentMethod.value = null
  }
})

watch(() => form.invoiceBuyerType, (buyerType) => {
  if (buyerType === 'company') {
    form.useMobileBarcode = false
    form.carrierValue = ''
  } else {
    form.companyTaxId = ''
    form.companyName = ''
  }
})

/**
 * 沿用顧客在購物車頁已經套用的優惠碼（alex 2026-09-03 PR #97 A1 裁定）。
 *
 * 載體是既有的<b>記憶體</b>購物車快取 —— 沒有新資料表、沒有 Cart 的 Coupon 欄位，
 * 也不放 URL 或 localStorage。這剛好給出裁定要求的邊界：
 *
 * - 重新整理／跨裝置：快取不存在，自然不沿用
 * - 登入／登出／換帳號：候選值與已套用的 state 都綁在身分鍵上，換人一起丟掉
 * - 購物車版本改變：下面明確比對 RowVersion，舊 quote 已失效就不沿用
 *
 * 沿用的只是「代碼」。金額仍由後端重算：這個代碼會進 useShippingOptions 的
 * query key 重新請求運費，建單時再由 Checkout 交易權威重驗全部規則。
 */
function adoptCartPageCoupon(nextValidation: CartValidationDto): void {
  const candidate = pendingCouponHandoff.value
  if (candidate === null || appliedCouponCode.value !== null) {
    return
  }

  if (candidate.rowVersion !== nextValidation.cart.rowVersion) {
    pendingCouponHandoff.value = null
    return
  }

  appliedCouponCode.value = candidate.code
  form.couponCode = candidate.code
  pendingCouponHandoff.value = null
}

/**
 * 讓優惠碼 state 跟上目前的身分（alex 2026-09-03 PR #97 第二輪第 1 點）。
 *
 * 身分化的 query cache 只隔離得了<b>快取</b>，隔離不了已經複製到元件裡的那一份：
 * CheckoutPage 掛載期間登入、登出或換帳號時，上一個身分的代碼會繼續被送進 Shipping
 * Options，最後也會進建單請求。所以身分鍵一變，已套用的代碼與輸入框都要清掉，
 * 候選值再從<b>新身分自己的</b>購物車快取重新取一份。
 *
 * 候選值必須在 revalidate <b>之前</b>取：useRevalidateCart 成功後會把權威購物車寫回
 * 同一個快取鍵，而優惠碼不保存在購物車上，覆蓋之後就再也讀不到顧客在 C-13 套的代碼。
 * 它也刻意活得比單次 loadCheckout 久 —— 見下面 loadCheckout 的說明。
 */
function syncCouponStateToIdentity(): void {
  if (couponIdentityKey.value === identityKey.value) {
    return
  }

  couponIdentityKey.value = identityKey.value
  appliedCouponCode.value = null
  form.couponCode = ''

  const cartPageCart = queryClient.getQueryData<CartDto>(cartIdentityKey.value)
  const code = cartPageCart?.coupon?.code
  pendingCouponHandoff.value = cartPageCart !== undefined && code
    ? { rowVersion: cartPageCart.rowVersion, code }
    : null
}

/**
 * 取得結帳所需的權威資料。**可以被重試呼叫多次**（初始化失敗時的重試按鈕）。
 *
 * 優惠碼的候選值刻意不放在這個函式的區域變數裡（alex 2026-09-03 PR #97 第二輪第 2 點）：
 * revalidate 成功、政策版本失敗時 Promise.all 會進錯誤分支，但權威購物車已經寫回快取，
 * 而它不帶優惠碼 —— 候選值若隨這次呼叫消失，重試時就再也讀不到顧客在 C-13 套的代碼，
 * 即使仍是同一 SPA、同一身分、同一 RowVersion。所以候選值由 syncCouponStateToIdentity
 * 維護，只有身分或版本不符時才清除。
 */
async function loadCheckout(): Promise<void> {
  if (!sessionStore.isIdentityConfirmed) {
    return
  }

  syncCouponStateToIdentity()

  const generation = ++loadGeneration
  isInitialLoading.value = true
  initialError.value = null
  validation.value = null
  policyVersions.value = null
  selectedShippingMethod.value = null

  try {
    const [nextValidation, nextPolicies] = await Promise.all([
      revalidateCart.mutateAsync(),
      getCheckoutPolicyVersions(),
    ])
    if (generation !== loadGeneration) {
      return
    }
    validation.value = nextValidation
    policyVersions.value = nextPolicies
    adoptCartPageCoupon(nextValidation)
  } catch (caught) {
    if (generation === loadGeneration) {
      initialError.value = caught
    }
  } finally {
    if (generation === loadGeneration) {
      isInitialLoading.value = false
    }
  }
}

watch(
  () => [sessionStore.status, sessionStore.user?.publicId] as const,
  () => { void loadCheckout() },
  { immediate: true },
)

function describeIssue(issue: CartIssueDto): string {
  return ISSUE_MESSAGES[issue.code] ?? '購物車仍有需要處理的項目，請回購物車重新檢查。'
}

function formatTwd(amount: number | string): string {
  return `NT$${Number(amount).toLocaleString('zh-Hant-TW')}`
}

function applyCoupon(): void {
  const normalized = form.couponCode.trim().toUpperCase()
  if (normalized) {
    appliedCouponCode.value = normalized
    selectedPaymentMethod.value = null
  }
}

function removeCoupon(): void {
  form.couponCode = ''
  appliedCouponCode.value = null
  selectedPaymentMethod.value = null
}

function buildRequest(): CreateOrderRequest {
  const option = selectedShippingOption.value!
  const policies = policyVersions.value!
  const currentCart = cart.value!

  return {
    cartPublicId: currentCart.publicId,
    cartRowVersion: currentCart.rowVersion,
    buyer: {
      email: form.buyerEmail.trim(),
      name: form.buyerName.trim(),
      phone: form.buyerPhone.trim(),
    },
    shipping: {
      methodCode: option.methodCode,
      address: option.requiresAddress
        ? {
            recipientName: form.recipientName.trim(),
            phone: form.recipientPhone.trim(),
            postalCode: form.postalCode.trim(),
            city: form.city.trim(),
            district: form.district.trim(),
            addressLine1: form.addressLine1.trim(),
            addressLine2: form.addressLine2.trim() || null,
          }
        : null,
      storePublicId: option.requiresStore ? selectedStorePublicId.value : null,
      deliveryNote: form.deliveryNote.trim() || null,
    },
    paymentMethod: selectedPaymentMethod.value!,
    couponCode: activeCouponCode.value,
    invoice: {
      type: 'simulated',
      buyerType: form.invoiceBuyerType,
      carrierType: form.invoiceBuyerType === 'personal' && form.useMobileBarcode
        ? 'MobileBarcode'
        : null,
      carrierValue: form.invoiceBuyerType === 'personal' && form.useMobileBarcode
        ? form.carrierValue.trim()
        : null,
      companyTaxId: form.invoiceBuyerType === 'company' ? form.companyTaxId.trim() : null,
      companyName: form.invoiceBuyerType === 'company' ? form.companyName.trim() : null,
    },
    acceptPolicyVersions: policies,
  }
}

function resolveIdempotencyKey(request: CreateOrderRequest): string {
  const signature = JSON.stringify(request)
  if (idempotencyState.value?.signature !== signature) {
    idempotencyState.value = { signature, key: createCorrelationId() }
  }
  return idempotencyState.value.key
}

function describeSubmitError(caught: unknown): string {
  if (!isApiError(caught)) {
    return '訂單建立失敗，請確認網路後重試。'
  }

  const messages: Record<string, string> = {
    concurrency_conflict: '購物車已在其他頁面變更，請回購物車重新檢查。',
    cart_not_ready: '購物車內容已改變，請回購物車重新檢查。',
    insufficient_stock: '部分商品庫存不足，請回購物車調整。',
    shipping_method_not_allowed: '此配送方式已無法使用，請重新載入結帳資料。',
    payment_method_not_allowed: '此付款方式已無法使用，請重新選擇。',
    idempotency_payload_conflict: '送出的結帳內容與先前重試不同，請重新載入結帳頁。',
    validation_failed: '部分欄位格式不正確，請檢查後重試。',
  }
  return messages[caught.code] ?? '訂單建立失敗，請稍後重試。'
}

async function submitOrder(): Promise<void> {
  if (!canSubmit.value || isSubmitting.value) {
    return
  }

  const request = buildRequest()
  const idempotencyKey = resolveIdempotencyKey(request)
  isSubmitting.value = true
  submitError.value = null
  submitCorrelationId.value = null

  let order: OrderDto
  try {
    order = await createOrder(request, idempotencyKey, getOrCreateGuestCartKey())
  } catch (caught) {
    submitError.value = describeSubmitError(caught)
    submitCorrelationId.value = isApiError(caught) ? caught.correlationId ?? null : null
    return
  } finally {
    isSubmitting.value = false
  }

  queryClient.removeQueries({ queryKey: ['cart'] })
  queryClient.removeQueries({ queryKey: ['shipping-options'] })

  if (!sessionStore.isAuthenticated) {
    createdOrderHandoff.value = { kind: 'guest', order }
    clearGuestCartKey()
    return
  }

  const routeName: MemberOrderRouteName =
    selectedPaymentMethod.value === 'cashOnDelivery' ? 'order-detail' : 'order-payment'
  createdOrderHandoff.value = {
    kind: 'member',
    order,
    routeName,
    navigationFailed: false,
  }

  try {
    await router.push({ name: routeName, params: { orderId: order.publicId } })
  } catch {
    createdOrderHandoff.value = {
      kind: 'member',
      order,
      routeName,
      navigationFailed: true,
    }
  }
}
</script>

<template>
  <section
    class="checkout-page"
    aria-labelledby="checkout-title"
  >
    <h1 id="checkout-title">
      結帳
    </h1>

    <section
      v-if="createdOrderHandoff"
      class="checkout-page__guest-success"
      aria-labelledby="order-created-title"
    >
      <h2 id="order-created-title">
        訂單已建立
      </h2>
      <p>訂單編號：<strong>{{ createdOrderHandoff.order.orderNumber }}</strong></p>
      <dl class="checkout-page__amount-breakdown">
        <div>
          <dt>商品小計：</dt>
          <dd>{{ formatTwd(createdOrderHandoff.order.amounts.merchandiseSubtotal) }}</dd>
        </div>
        <div>
          <dt>優惠折扣：</dt>
          <dd>−{{ formatTwd(createdOrderHandoff.order.amounts.itemDiscountTotal) }}</dd>
        </div>
        <div>
          <dt>配送費：</dt>
          <dd>{{ formatTwd(createdOrderHandoff.order.amounts.shippingFee) }}</dd>
        </div>
        <div>
          <dt>組裝費：</dt>
          <dd>{{ formatTwd(createdOrderHandoff.order.amounts.assemblyFee) }}</dd>
        </div>
        <div class="checkout-page__amount-total">
          <dt>應付總額：</dt>
          <dd>{{ formatTwd(createdOrderHandoff.order.amounts.grandTotal) }}</dd>
        </div>
      </dl>

      <template v-if="createdOrderHandoff.kind === 'guest'">
        <p>為保護訂單資料，訪客需以訂單編號與結帳 Email 完成一次性驗證，才能繼續付款或查看訂單。</p>
        <RouterLink to="/guest-orders/access">
          驗證訂單後繼續付款
        </RouterLink>
      </template>

      <template v-else>
        <p v-if="createdOrderHandoff.navigationFailed">
          訂單已經建立成功，只是沒能自動開啟下一頁。
        </p>
        <RouterLink
          :to="{
            name: createdOrderHandoff.routeName,
            params: { orderId: createdOrderHandoff.order.publicId },
          }"
        >
          {{ createdOrderHandoff.routeName === 'order-payment' ? '前往付款' : '查看訂單' }}
        </RouterLink>
      </template>
    </section>

    <div
      v-else-if="sessionStore.status === 'error'"
      class="checkout-page__alert"
      role="alert"
    >
      <p>無法確認登入狀態，暫時不能結帳。</p>
      <button
        type="button"
        @click="sessionStore.refresh()"
      >
        重試
      </button>
    </div>

    <LoadingState
      v-else-if="!sessionStore.isIdentityConfirmed || isInitialLoading"
      label="結帳資料載入中"
    />

    <ErrorState
      v-else-if="initialError"
      :correlation-id="isApiError(initialError) ? initialError.correlationId : undefined"
      @retry="loadCheckout"
    />

    <EmptyState
      v-else-if="cart && cart.items.length === 0"
      title="購物車是空的"
      description="請先加入商品再進行結帳。"
    >
      <RouterLink to="/cart">
        返回購物車
      </RouterLink>
    </EmptyState>

    <section
      v-else-if="validation && !validation.isCheckoutReady"
      class="checkout-page__blocked"
      aria-labelledby="checkout-blocked-title"
    >
      <h2 id="checkout-blocked-title">
        購物車需要先處理
      </h2>
      <ul>
        <li
          v-for="(issue, index) in validation.issues"
          :key="`${issue.code}-${index}`"
        >
          {{ describeIssue(issue) }}
        </li>
      </ul>
      <RouterLink to="/cart">
        返回購物車
      </RouterLink>
    </section>

    <form
      v-else-if="cart && policyVersions"
      class="checkout-page__submit"
      @submit.prevent="submitOrder"
    >
      <section aria-labelledby="buyer-title">
        <h2 id="buyer-title">
          聯絡資料
        </h2>
        <div class="checkout-page__fields">
          <label for="buyer-email">Email</label>
          <input
            id="buyer-email"
            v-model="form.buyerEmail"
            type="email"
            autocomplete="email"
            maxlength="320"
            required
          >
          <label for="buyer-name">姓名</label>
          <input
            id="buyer-name"
            v-model="form.buyerName"
            autocomplete="name"
            maxlength="100"
            required
          >
          <label for="buyer-phone">電話</label>
          <input
            id="buyer-phone"
            v-model="form.buyerPhone"
            autocomplete="tel"
            maxlength="32"
            required
          >
        </div>
      </section>

      <section aria-labelledby="shipping-title">
        <h2 id="shipping-title">
          配送方式
        </h2>
        <LoadingState
          v-if="isShippingPending"
          label="配送方式載入中"
        />
        <ErrorState
          v-else-if="isShippingError"
          :correlation-id="isApiError(shippingError) ? shippingError.correlationId : undefined"
          @retry="refetchShipping"
        />
        <ShippingOptionList
          v-else-if="shippingOptions"
          v-model="selectedShippingMethod"
          :options="shippingOptions.options"
          selectable
        />

        <div
          v-if="selectedShippingOption?.requiresAddress"
          class="checkout-page__fields checkout-page__address"
        >
          <label for="recipient-name">收件人</label>
          <input
            id="recipient-name"
            v-model="form.recipientName"
            maxlength="100"
            required
          >
          <label for="recipient-phone">收件電話</label>
          <input
            id="recipient-phone"
            v-model="form.recipientPhone"
            maxlength="32"
            required
          >
          <label for="postal-code">郵遞區號</label>
          <input
            id="postal-code"
            v-model="form.postalCode"
            maxlength="16"
            required
          >
          <label for="city">縣市</label>
          <input
            id="city"
            v-model="form.city"
            maxlength="50"
            required
          >
          <label for="district">行政區</label>
          <input
            id="district"
            v-model="form.district"
            maxlength="50"
            required
          >
          <label for="address-line1">地址</label>
          <input
            id="address-line1"
            v-model="form.addressLine1"
            maxlength="300"
            required
          >
          <label for="address-line2">地址補充</label>
          <input
            id="address-line2"
            v-model="form.addressLine2"
            maxlength="300"
          >
        </div>

        <ConvenienceStorePicker
          v-if="selectedShippingOption?.requiresStore"
          v-model="selectedStorePublicId"
          v-model:selected-summary="selectedStoreSummary"
        />

        <label
          v-if="selectedShippingOption"
          for="delivery-note"
        >配送備註</label>
        <textarea
          v-if="selectedShippingOption"
          id="delivery-note"
          v-model="form.deliveryNote"
          maxlength="500"
        />
      </section>

      <section
        v-if="selectedShippingOption"
        aria-labelledby="payment-title"
      >
        <h2 id="payment-title">
          付款方式
        </h2>
        <p
          v-if="allowedPaymentMethods.length === 0"
          role="alert"
        >
          此配送方式目前沒有可用付款方式。
        </p>
        <label
          v-for="method in allowedPaymentMethods"
          :key="method"
          class="checkout-page__choice"
        >
          <input
            v-model="selectedPaymentMethod"
            type="radio"
            name="payment-method"
            :value="method"
          >
          {{ PAYMENT_LABELS[method] }}
        </label>
      </section>

      <section aria-labelledby="coupon-title">
        <h2 id="coupon-title">
          優惠券
        </h2>
        <label for="coupon-code">優惠碼（選填）</label>
        <input
          id="coupon-code"
          v-model="form.couponCode"
          maxlength="64"
        >
        <button
          type="button"
          data-test="apply-coupon"
          :disabled="!form.couponCode.trim() || isShippingPending"
          @click="applyCoupon"
        >
          套用優惠券
        </button>
        <button
          v-if="activeCouponCode"
          type="button"
          data-test="remove-coupon"
          @click="removeCoupon"
        >
          移除優惠券
        </button>
        <p v-if="activeCouponCode && !isShippingError">
          已套用 {{ activeCouponCode }}；配送費與付款方式已由後端重新試算。
        </p>
        <p>優惠資格與最終折扣會由後端在建立訂單時重新驗證。</p>
      </section>

      <section aria-labelledby="invoice-title">
        <h2 id="invoice-title">
          模擬發票
        </h2>
        <label class="checkout-page__choice">
          <input
            v-model="form.invoiceBuyerType"
            type="radio"
            name="invoice-buyer-type"
            value="personal"
          >
          個人
        </label>
        <label class="checkout-page__choice">
          <input
            v-model="form.invoiceBuyerType"
            type="radio"
            name="invoice-buyer-type"
            value="company"
          >
          公司
        </label>

        <template v-if="form.invoiceBuyerType === 'personal'">
          <label class="checkout-page__choice">
            <input
              v-model="form.useMobileBarcode"
              type="checkbox"
            >
            使用手機條碼載具
          </label>
          <label
            v-if="form.useMobileBarcode"
            for="carrier-value"
          >手機條碼</label>
          <input
            v-if="form.useMobileBarcode"
            id="carrier-value"
            v-model="form.carrierValue"
            maxlength="64"
            required
          >
        </template>
        <div
          v-else
          class="checkout-page__fields"
        >
          <label for="company-tax-id">統一編號</label>
          <input
            id="company-tax-id"
            v-model="form.companyTaxId"
            inputmode="numeric"
            pattern="[0-9]{8}"
            maxlength="8"
            required
          >
          <label for="company-name">公司名稱</label>
          <input
            id="company-name"
            v-model="form.companyName"
            maxlength="160"
            required
          >
        </div>
      </section>

      <section aria-labelledby="policy-title">
        <h2 id="policy-title">
          政策確認
        </h2>
        <label class="checkout-page__choice">
          <input
            id="accept-terms"
            v-model="form.acceptTerms"
            type="checkbox"
            required
          >
          我同意服務條款（版本 {{ policyVersions.terms }}）
        </label>
        <label class="checkout-page__choice">
          <input
            id="accept-return"
            v-model="form.acceptReturn"
            type="checkbox"
            required
          >
          我同意退換貨政策（版本 {{ policyVersions.return }}）
        </label>
        <label class="checkout-page__choice">
          <input
            id="accept-privacy"
            v-model="form.acceptPrivacy"
            type="checkbox"
            required
          >
          我同意隱私權政策（版本 {{ policyVersions.privacy }}）
        </label>
      </section>

      <aside class="checkout-page__summary">
        <p>商品預估合計：{{ formatTwd(cart.amounts.totalEstimate) }}</p>
        <p v-if="selectedShippingOption">
          配送費：{{ formatTwd(selectedShippingOption.fee) }}
        </p>
        <p>最終金額、折扣、運費及庫存以後端建立訂單時重新計算為準。</p>
      </aside>

      <p
        v-if="submitError"
        class="checkout-page__alert"
        role="alert"
      >
        {{ submitError }}
        <span v-if="submitCorrelationId">追蹤編號：{{ submitCorrelationId }}</span>
      </p>

      <button
        type="submit"
        :disabled="!canSubmit || isSubmitting"
      >
        {{ isSubmitting ? '建立訂單中…' : '確認建立訂單' }}
      </button>
    </form>
  </section>
</template>

<style scoped>
.checkout-page {
  max-width: 52rem;
}

.checkout-page__submit {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}

.checkout-page__submit > section,
.checkout-page__summary,
.checkout-page__guest-success,
.checkout-page__blocked {
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.5rem;
}

.checkout-page__submit h2,
.checkout-page__guest-success h2,
.checkout-page__blocked h2 {
  margin-top: 0;
  font-size: 1.125rem;
}

.checkout-page__fields {
  display: grid;
  grid-template-columns: minmax(7rem, 10rem) minmax(0, 1fr);
  gap: 0.75rem;
  align-items: center;
}

.checkout-page__amount-breakdown {
  display: grid;
  gap: 0.375rem;
  max-width: 24rem;
  margin: 1rem 0;
}

.checkout-page__amount-breakdown > div {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
}

.checkout-page__amount-breakdown dd {
  margin: 0;
  font-variant-numeric: tabular-nums;
}

.checkout-page__amount-total {
  padding-top: 0.5rem;
  border-top: 1px solid #d1d5db;
  font-weight: 700;
}

.checkout-page__address {
  margin-top: 1rem;
}

.checkout-page__choice {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-block: 0.5rem;
}

.checkout-page__summary {
  background: #f8fafc;
}

.checkout-page__alert {
  padding: 0.75rem 1rem;
  border-radius: 0.5rem;
  background: #fee2e2;
  color: #991b1b;
}

@media (max-width: 40rem) {
  .checkout-page__fields {
    grid-template-columns: 1fr;
    gap: 0.25rem;
  }
}
</style>
