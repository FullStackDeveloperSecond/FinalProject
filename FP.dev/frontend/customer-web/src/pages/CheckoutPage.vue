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
  getGuestCheckoutEmailVerificationStatus,
  getCheckoutPolicyVersions,
  requestGuestCheckoutEmailVerification,
  verifyGuestCheckoutEmail,
  type AcceptedPolicyVersions,
  type CreateOrderRequest,
  type OrderDto,
  type PaymentMethod,
} from '../features/checkout/api'
import ShippingOptionList from '../features/shipping/components/ShippingOptionList.vue'
import ConvenienceStorePicker from '../features/shipping/components/ConvenienceStorePicker.vue'
import { useShippingOptions } from '../features/shipping/useShipping'
import type { ConvenienceStoreOptionDto, ShippingOptionDto } from '../features/shipping/types'
import { districtsForCity, TAIWAN_CITIES } from '../features/shipping/taiwanAdministrativeAreas'
import { fetchAddresses, fetchProfile, type MemberAddress } from '../features/members/api'
import LegalDemoPage from './LegalDemoPage.vue'

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

type CreatedOrderHandoff = {
  order: OrderDto
  routeName: MemberOrderRouteName
  navigationFailed: boolean
}

type RecentOrderReceipt = { handoff: CreatedOrderHandoff, expiresAt: number }

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
const demoAutofillEnabled = import.meta.env.DEV && ['localhost', '127.0.0.1', '[::1]'].includes(window.location.hostname)
function fillDemoRecipient(): void {
  Object.assign(form, { buyerName: '展示測試員', buyerPhone: '0912345678', recipientName: '展示測試員', recipientPhone: '0912345678', postalCode: '100', city: '臺北市', district: '中正區', addressLine1: '展示路 1 號（虛構測試地址）', addressLine2: '', deliveryNote: '僅供展示，請勿實際配送。' })
}
const queryClient = useQueryClient()
const cartIdentityKey = useCartIdentityKey()
const identityKey = computed(() => cartIdentityKey.value.join(' '))
const sessionStore = useSessionStore()
const revalidateCart = useRevalidateCart()

const validation = ref<CartValidationDto | null>(null)
const policyVersions = ref<AcceptedPolicyVersions | null>(null)
const policyDialog = ref<HTMLDialogElement | null>(null)
const readingPolicy = ref<'terms' | 'privacy' | 'return'>('terms')
const policyNames = { terms: '服務條款', return: '退換貨政策', privacy: '隱私權政策' } as const
function openPolicy(kind: typeof readingPolicy.value): void {
  readingPolicy.value = kind
  policyDialog.value?.showModal()
}
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
const guestVerificationRequestId = ref<string | null>(null)
const guestVerificationCode = ref('')
const verifiedGuestEmail = ref<string | null>(null)
const isGuestVerificationBusy = ref(false)
const guestVerificationMessage = ref<string | null>(null)
const memberAddresses = ref<MemberAddress[]>([])
const selectedAddressId = ref('')
const memberDetailsMessage = ref<string | null>(null)
const phonePattern = '09[0-9]{8}'
const emailPattern = '[^\\s@]+@[^\\s@]+\\.[^\\s@]+'
const carrierPattern = '/[A-Z0-9+.\\-]{7}'
const postalPattern = '[0-9]{3}(?:[0-9]{2,3})?'
const cities: readonly string[] = TAIWAN_CITIES
const matches = (pattern: string, value: string) => new RegExp(`^${pattern}$`, 'u').test(value.trim())

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
  invoiceBuyerType: 'personal',
  useMobileBarcode: false,
  carrierValue: '',
  companyTaxId: '',
  companyName: '',
  acceptTerms: false,
  acceptReturn: false,
  acceptPrivacy: false,
})
const selectedDistricts = computed<readonly string[]>(() => districtsForCity(form.city))

// 即時填寫提示不取代伺服器驗證。
const touchedFields = reactive<Record<string, boolean>>({})
const fieldErrors = computed<Record<string, string>>(() => {
  const errors: Record<string, string> = {}
  function check(id: string, value: string, label: string, pattern?: string, message?: string) {
    if (!value.trim()) errors[id] = `${label}為必填。`
    else if (pattern && !matches(pattern, value)) errors[id] = message ?? `${label}格式不正確。`
  }
  const mobileError = '手機號碼輸入錯誤，請輸入 09 開頭的 10 位數字。'
  check('buyer-email', form.buyerEmail, '電子郵件', emailPattern, '電子郵件格式不正確，例如：name@example.com。')
  check('buyer-name', form.buyerName, '姓名')
  check('buyer-phone', form.buyerPhone, '聯絡手機號碼', phonePattern, mobileError)
  if (selectedShippingOption.value?.requiresAddress) {
    check('recipient-name', form.recipientName, '收件人')
    check('recipient-phone', form.recipientPhone, '收件手機號碼', phonePattern, mobileError)
    check('postal-code', form.postalCode, '郵遞區號', postalPattern, '郵遞區號請輸入 3、5 或 6 位數字。')
    if (!cities.includes(form.city)) errors.city = '請選擇縣市。'
    if (!selectedDistricts.value.includes(form.district)) errors.district = '請選擇此縣市所屬的行政區。'
    check('address-line1', form.addressLine1, '地址')
  }
  if (form.invoiceBuyerType === 'company') {
    check('company-tax-id', form.companyTaxId, '統一編號', '[0-9]{8}', '統一編號請輸入 8 位數字。')
    check('company-name', form.companyName, '公司抬頭')
  } else if (form.useMobileBarcode) {
    check('carrier-value', form.carrierValue, '手機條碼', carrierPattern, '手機條碼須為 / 開頭，後接 7 位大寫英文字母、數字或 + - .。')
  }
  return errors
})
function touchField(event: Event): void {
  const target = event.target
  if (target instanceof HTMLInputElement || target instanceof HTMLSelectElement || target instanceof HTMLTextAreaElement) {
    touchedFields[target.id] = true
  }
}
function fieldFeedback(id: string) {
  const invalid = Boolean(touchedFields[id] && fieldErrors.value[id])
  return { 'aria-invalid': invalid, 'aria-describedby': invalid ? `${id}-error` : undefined }
}
const visibleFieldErrors = computed(() => Object.entries(fieldErrors.value).filter(([id]) => touchedFields[id]))

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
  && matches(phonePattern, form.recipientPhone)
  && matches(postalPattern, form.postalCode)
  && cities.includes(form.city)
  && selectedDistricts.value.includes(form.district)
  && form.addressLine1.trim(),
))

const isInvoiceComplete = computed(() => {
  if (form.invoiceBuyerType === 'company') {
    return /^\d{8}$/.test(form.companyTaxId.trim()) && Boolean(form.companyName.trim())
  }
  return !form.useMobileBarcode || matches(carrierPattern, form.carrierValue)
})

const normalizedBuyerEmail = computed(() => form.buyerEmail.trim().toLowerCase())
const isGuestEmailVerified = computed(() =>
  sessionStore.isAuthenticated || (
    Boolean(verifiedGuestEmail.value)
    && verifiedGuestEmail.value === normalizedBuyerEmail.value
  ),
)

const canSubmit = computed(() => {
  const option = selectedShippingOption.value
  if (!cart.value || !policyVersions.value || !validation.value?.isCheckoutReady || !option?.isEligible) {
    return false
  }
  if (option.requiresAddress === option.requiresStore) {
    return false
  }
  if (Object.keys(fieldErrors.value).length > 0) {
    return false
  }
  if (!isGuestEmailVerified.value) {
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

async function refreshGuestEmailVerification(): Promise<void> {
  if (sessionStore.isAuthenticated) {
    verifiedGuestEmail.value = null
    return
  }
  const owner = identityKey.value
  try {
    const status = await getGuestCheckoutEmailVerificationStatus(getOrCreateGuestCartKey())
    if (owner !== identityKey.value || sessionStore.isAuthenticated) return
    verifiedGuestEmail.value = status.verified && status.email
      ? status.email.toLowerCase()
      : null
    if (status.verified) {
      guestVerificationMessage.value = '信箱已完成驗證。'
    }
  } catch {
    verifiedGuestEmail.value = null
  }
}

async function sendGuestEmailVerification(): Promise<void> {
  if (!matches(emailPattern, normalizedBuyerEmail.value) || isGuestVerificationBusy.value) {
    return
  }
  isGuestVerificationBusy.value = true
  guestVerificationMessage.value = null
  verifiedGuestEmail.value = null
  const email = normalizedBuyerEmail.value
  const owner = identityKey.value
  try {
    const accepted = await requestGuestCheckoutEmailVerification(
      email,
      getOrCreateGuestCartKey(),
    )
    if (owner !== identityKey.value || email !== normalizedBuyerEmail.value) return
    guestVerificationRequestId.value = accepted.requestPublicId
    guestVerificationCode.value = ''
    guestVerificationMessage.value = '已受理寄信，請稍候查看收件匣與垃圾郵件，開啟驗證連結或輸入六位數驗證碼。'
  } catch {
    guestVerificationMessage.value = '驗證信暫時無法寄出，請稍後再試。'
  } finally {
    isGuestVerificationBusy.value = false
  }
}

async function confirmGuestEmailVerification(): Promise<void> {
  if (!guestVerificationRequestId.value || !/^\d{6}$/.test(guestVerificationCode.value)) {
    return
  }
  isGuestVerificationBusy.value = true
  guestVerificationMessage.value = null
  const email = normalizedBuyerEmail.value
  const owner = identityKey.value
  try {
    await verifyGuestCheckoutEmail(
      guestVerificationRequestId.value,
      guestVerificationCode.value,
      getOrCreateGuestCartKey(),
    )
    if (owner !== identityKey.value || email !== normalizedBuyerEmail.value) return
    verifiedGuestEmail.value = email
    guestVerificationMessage.value = '信箱已完成驗證。'
  } catch {
    verifiedGuestEmail.value = null
    guestVerificationMessage.value = '驗證碼無效或已過期，請重新確認。'
  } finally {
    isGuestVerificationBusy.value = false
  }
}

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

watch(normalizedBuyerEmail, (email) => {
  guestVerificationRequestId.value = null
  guestVerificationCode.value = ''
  if (verifiedGuestEmail.value && verifiedGuestEmail.value !== email) {
    guestVerificationMessage.value = 'Email 已變更，請重新完成驗證。'
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
  // A retry may load a new policy version; previous consent cannot silently carry over.
  form.acceptTerms = false
  form.acceptReturn = false
  form.acceptPrivacy = false
  if (policyDialog.value?.open) policyDialog.value.close()
  policyVersions.value = null
  selectedShippingMethod.value = null

  try {
    const [nextValidation, nextPolicies] = await Promise.all([
      revalidateCart.mutateAsync(undefined),
      getCheckoutPolicyVersions(),
    ])
    if (generation !== loadGeneration) {
      return
    }
    validation.value = nextValidation
    policyVersions.value = nextPolicies
    const receipt = queryClient.getQueryData<RecentOrderReceipt>(receiptKey())
    if (nextValidation.cart.items.length === 0 && receipt && receipt.expiresAt > Date.now()) {
      createdOrderHandoff.value = receipt.handoff
    }
    adoptCartPageCoupon(nextValidation)
    await refreshGuestEmailVerification()
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

function applyMemberAddress(): void {
  const address = memberAddresses.value.find(item => item.publicId === selectedAddressId.value)
  if (!address) return
  form.recipientName = address.recipientName
  form.recipientPhone = address.phone
  form.postalCode = address.postalCode
  form.city = address.city.replace(/^台/, '臺')
  form.district = address.district
  form.addressLine1 = address.addressLine1
  form.addressLine2 = address.addressLine2 ?? ''
}

let memberDetailsGeneration = 0
watch(
  () => [sessionStore.status, sessionStore.user?.publicId] as const,
  async () => {
    const generation = ++memberDetailsGeneration
    memberAddresses.value = []
    selectedAddressId.value = ''
    memberDetailsMessage.value = null
    createdOrderHandoff.value = null
    for (const key of ['buyerEmail', 'buyerName', 'buyerPhone', 'recipientName', 'recipientPhone',
      'postalCode', 'city', 'district', 'addressLine1', 'addressLine2', 'deliveryNote'] as const) {
      form[key] = ''
    }
    verifiedGuestEmail.value = null
    guestVerificationRequestId.value = null
    guestVerificationCode.value = ''
    if (!sessionStore.isAuthenticated) return
    const [profile, addresses] = await Promise.allSettled([fetchProfile(), fetchAddresses()])
    if (generation !== memberDetailsGeneration) return
    if (profile.status === 'fulfilled') {
      if (!form.buyerName) form.buyerName = profile.value.displayName
      if (!form.buyerPhone) form.buyerPhone = profile.value.phone ?? ''
    }
    if (addresses.status === 'fulfilled') {
      memberAddresses.value = addresses.value
      const defaultAddress = addresses.value.find(address => address.isDefault)
      if (defaultAddress && !form.recipientName && !form.addressLine1 && !form.recipientPhone
        && !form.postalCode && !form.city && !form.district && !form.addressLine2) {
        selectedAddressId.value = defaultAddress.publicId
        applyMemberAddress()
      }
    }
    if (profile.status === 'rejected' || addresses.status === 'rejected') {
      memberDetailsMessage.value = '部分會員資料暫時無法載入，請手動填寫聯絡與收件資料。'
    }
  },
  { immediate: true },
)

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

function changeCity(): void {
  form.district = ''
  touchedFields.district = false
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
    guest_checkout_email_verification_required: '訪客信箱尚未驗證、已過期，或 Email 已變更，請重新驗證。',
  }
  return messages[caught.code] ?? '訂單建立失敗，請稍後重試。'
}

async function submitOrder(): Promise<void> {
  if (!canSubmit.value || isSubmitting.value) {
    return
  }

  const request = buildRequest()
  const owner = identityKey.value
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

  if (owner !== identityKey.value || !sessionStore.isIdentityConfirmed) return

  queryClient.removeQueries({ queryKey: ['cart'] })
  queryClient.removeQueries({ queryKey: ['shipping-options'] })

  const routeName: MemberOrderRouteName =
    selectedPaymentMethod.value === 'cashOnDelivery' ? 'order-detail' : 'order-payment'
  createdOrderHandoff.value = {
    order,
    routeName,
    navigationFailed: false,
  }

  if (!sessionStore.isAuthenticated) {
    clearGuestCartKey()
  }

  queryClient.setQueryData<RecentOrderReceipt>(receiptKey(), {
    handoff: createdOrderHandoff.value,
    expiresAt: Date.now() + 30 * 60 * 1000,
  })

  try {
    await router.push({ name: routeName, params: { orderId: order.publicId } })
  } catch {
    createdOrderHandoff.value = {
      order,
      routeName,
      navigationFailed: true,
    }
  }
}

function receiptKey() {
  return sessionStore.isAuthenticated && sessionStore.user
    ? ['checkout-receipt', 'member', sessionStore.user.publicId]
    : ['checkout-receipt', 'guest', getOrCreateGuestCartKey()]
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

      <p v-if="createdOrderHandoff.navigationFailed">
        訂單已經建立成功，只是沒能自動開啟下一頁。
      </p>
      <RouterLink
        class="page-action"
        :to="{
          name: createdOrderHandoff.routeName,
          params: { orderId: createdOrderHandoff.order.publicId },
        }"
      >
        {{ createdOrderHandoff.routeName === 'order-payment' ? '前往付款' : '查看訂單' }}
      </RouterLink>
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
      @input.capture="touchField"
      @change.capture="touchField"
      @focusout.capture="touchField"
      @submit.prevent="submitOrder"
    >
      <section aria-labelledby="buyer-title">
        <h2 id="buyer-title">
          聯絡資料
        </h2>
        <button
          v-if="demoAutofillEnabled"
          type="button"
          @click="fillDemoRecipient"
        >
          填入 Demo 聯絡與收件資料（覆寫欄位）
        </button>
        <p>標示 * 的欄位為必填；其餘標示為選填。手機號碼請輸入 09 開頭的 10 位數字。</p>
        <ul
          v-if="visibleFieldErrors.length"
          class="checkout-page__alert"
          aria-live="polite"
          aria-label="欄位格式提醒"
        >
          <li
            v-for="[id, message] in visibleFieldErrors"
            :key="id"
          >
            <a :href="`#${id}`">{{ message }}</a>
          </li>
        </ul>
        <p
          v-if="memberDetailsMessage"
          role="status"
        >
          {{ memberDetailsMessage }}
        </p>
        <div class="checkout-page__fields">
          <label for="buyer-email">電子郵件 *</label>
          <input
            id="buyer-email"
            v-bind="fieldFeedback('buyer-email')"
            v-model="form.buyerEmail"
            type="email"
            autocomplete="email"
            maxlength="320"
            required
          >
          <p
            v-if="touchedFields['buyer-email'] && fieldErrors['buyer-email']"
            id="buyer-email-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['buyer-email'] }}
          </p>
          <template v-if="!sessionStore.isAuthenticated">
            <button
              type="button"
              data-test="send-guest-email-verification"
              :disabled="!matches(emailPattern, normalizedBuyerEmail) || isGuestVerificationBusy"
              @click="sendGuestEmailVerification"
            >
              {{ isGuestVerificationBusy ? '處理中…' : '寄送驗證信' }}
            </button>
            <template v-if="guestVerificationRequestId && !isGuestEmailVerified">
              <label for="guest-email-code">六位數驗證碼（使用驗證連結時可不填）</label>
              <input
                id="guest-email-code"
                v-model="guestVerificationCode"
                inputmode="numeric"
                pattern="[0-9]{6}"
                maxlength="6"
              >
              <button
                type="button"
                data-test="confirm-guest-email-verification"
                :disabled="!/^\d{6}$/.test(guestVerificationCode) || isGuestVerificationBusy"
                @click="confirmGuestEmailVerification"
              >
                確認驗證碼
              </button>
            </template>
            <p
              v-if="guestVerificationMessage"
              :role="isGuestEmailVerified ? 'status' : undefined"
            >
              {{ guestVerificationMessage }}
            </p>
            <button
              v-if="!isGuestEmailVerified"
              type="button"
              data-test="refresh-guest-email-verification"
              @click="refreshGuestEmailVerification"
            >
              我已開啟驗證連結，重新確認
            </button>
          </template>
          <label for="buyer-name">姓名 *</label>
          <input
            id="buyer-name"
            v-bind="fieldFeedback('buyer-name')"
            v-model="form.buyerName"
            autocomplete="name"
            maxlength="100"
            required
          >
          <p
            v-if="touchedFields['buyer-name'] && fieldErrors['buyer-name']"
            id="buyer-name-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['buyer-name'] }}
          </p>
          <label for="buyer-phone">聯絡手機號碼 *</label>
          <input
            id="buyer-phone"
            v-bind="fieldFeedback('buyer-phone')"
            v-model="form.buyerPhone"
            type="tel"
            :pattern="phonePattern"
            placeholder="例如：0912345678"
            autocomplete="tel"
            maxlength="32"
            required
          >
          <p
            v-if="touchedFields['buyer-phone'] && fieldErrors['buyer-phone']"
            id="buyer-phone-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['buyer-phone'] }}
          </p>
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
          <template v-if="sessionStore.isAuthenticated && memberAddresses.length">
            <label for="saved-address">已儲存的收件地址</label>
            <select
              id="saved-address"
              v-model="selectedAddressId"
              @change="applyMemberAddress"
            >
              <option value="">
                自行填寫
              </option>
              <option
                v-for="address in memberAddresses"
                :key="address.publicId"
                :value="address.publicId"
              >
                {{ address.label }}{{ address.isDefault ? '（預設）' : '' }}
              </option>
            </select>
          </template>
          <label for="recipient-name">收件人 *</label>
          <input
            id="recipient-name"
            v-bind="fieldFeedback('recipient-name')"
            v-model="form.recipientName"
            maxlength="100"
            required
          >
          <p
            v-if="touchedFields['recipient-name'] && fieldErrors['recipient-name']"
            id="recipient-name-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['recipient-name'] }}
          </p>
          <label for="recipient-phone">收件手機號碼 *</label>
          <input
            id="recipient-phone"
            v-bind="fieldFeedback('recipient-phone')"
            v-model="form.recipientPhone"
            type="tel"
            :pattern="phonePattern"
            placeholder="例如：0912345678"
            maxlength="32"
            required
          >
          <p
            v-if="touchedFields['recipient-phone'] && fieldErrors['recipient-phone']"
            id="recipient-phone-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['recipient-phone'] }}
          </p>
          <label for="postal-code">郵遞區號 *</label>
          <input
            id="postal-code"
            v-bind="fieldFeedback('postal-code')"
            v-model="form.postalCode"
            inputmode="numeric"
            :pattern="postalPattern"
            placeholder="3、5 或 6 位數字"
            maxlength="6"
            required
          >
          <p
            v-if="touchedFields['postal-code'] && fieldErrors['postal-code']"
            id="postal-code-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['postal-code'] }}
          </p>
          <label for="city">縣市 *</label>
          <select
            id="city"
            v-bind="fieldFeedback('city')"
            v-model="form.city"
            required
            @change="changeCity"
          >
            <option value="">
              請選擇縣市
            </option>
            <option
              v-for="city in cities"
              :key="city"
              :value="city"
            >
              {{ city }}
            </option>
          </select>
          <p
            v-if="touchedFields['city'] && fieldErrors['city']"
            id="city-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['city'] }}
          </p>
          <label for="district">行政區 *</label>
          <select
            id="district"
            v-bind="fieldFeedback('district')"
            v-model="form.district"
            :disabled="!form.city"
            required
          >
            <option value="">
              {{ form.city ? '請選擇行政區' : '請先選擇縣市' }}
            </option>
            <option
              v-for="district in selectedDistricts"
              :key="district"
              :value="district"
            >
              {{ district }}
            </option>
          </select>
          <p
            v-if="touchedFields['district'] && fieldErrors['district']"
            id="district-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['district'] }}
          </p>
          <label for="address-line1">地址 *</label>
          <input
            id="address-line1"
            v-bind="fieldFeedback('address-line1')"
            v-model="form.addressLine1"
            maxlength="300"
            required
          >
          <p
            v-if="touchedFields['address-line1'] && fieldErrors['address-line1']"
            id="address-line1-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['address-line1'] }}
          </p>
          <label for="address-line2">地址補充（選填）</label>
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
        >配送備註（選填）</label>
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
        <p
          v-if="activeCouponCode && isShippingPending"
          role="status"
        >
          正在確認 {{ activeCouponCode }} 的優惠資格與金額…
        </p>
        <p v-else-if="activeCouponCode && !isShippingError && selectedShippingOption">
          已套用 {{ activeCouponCode }}，折扣 {{ formatTwd(selectedShippingOption.amounts.itemDiscountTotal) }}；應付總額已更新。
        </p>
        <p v-else-if="activeCouponCode">
          已從購物車帶入優惠碼 {{ activeCouponCode }}；選擇配送方式後會顯示折扣結果。
        </p>
        <p v-else>
          未套用優惠券。如需使用優惠碼，請返回購物車輸入。
        </p>
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
          >手機條碼 *</label>
          <input
            v-if="form.useMobileBarcode"
            id="carrier-value"
            v-bind="fieldFeedback('carrier-value')"
            v-model="form.carrierValue"
            :pattern="carrierPattern"
            placeholder="例如：/ABC1234"
            maxlength="64"
            required
          >
          <p
            v-if="touchedFields['carrier-value'] && fieldErrors['carrier-value']"
            id="carrier-value-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['carrier-value'] }}
          </p>
        </template>
        <div
          v-else
          class="checkout-page__fields"
        >
          <label for="company-tax-id">統一編號 *</label>
          <input
            id="company-tax-id"
            v-bind="fieldFeedback('company-tax-id')"
            v-model="form.companyTaxId"
            inputmode="numeric"
            pattern="[0-9]{8}"
            maxlength="8"
            required
          >
          <p
            v-if="touchedFields['company-tax-id'] && fieldErrors['company-tax-id']"
            id="company-tax-id-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['company-tax-id'] }}
          </p>
          <label for="company-name">公司抬頭 *</label>
          <input
            id="company-name"
            v-bind="fieldFeedback('company-name')"
            v-model="form.companyName"
            maxlength="160"
            required
          >
          <p
            v-if="touchedFields['company-name'] && fieldErrors['company-name']"
            id="company-name-error"
            class="checkout-page__field-error"
            aria-live="polite"
          >
            {{ fieldErrors['company-name'] }}
          </p>
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
          我同意服務條款 *
        </label>
        <button
          type="button"
          data-test="read-terms"
          aria-haspopup="dialog"
          @click="openPolicy('terms')"
        >
          服務條款詳細確認
        </button>
        <label class="checkout-page__choice">
          <input
            id="accept-return"
            v-model="form.acceptReturn"
            type="checkbox"
            required
          >
          我同意退換貨政策 *
        </label>
        <button
          type="button"
          data-test="read-return"
          aria-haspopup="dialog"
          @click="openPolicy('return')"
        >
          退換貨政策詳細確認
        </button>
        <label class="checkout-page__choice">
          <input
            id="accept-privacy"
            v-model="form.acceptPrivacy"
            type="checkbox"
            required
          >
          我同意隱私權政策 *
        </label>
        <button
          type="button"
          data-test="read-privacy"
          aria-haspopup="dialog"
          @click="openPolicy('privacy')"
        >
          隱私權政策詳細確認
        </button>
      </section>

      <aside class="checkout-page__summary">
        <template v-if="selectedShippingOption">
          <p>商品小計：{{ formatTwd(selectedShippingOption.amounts.merchandiseSubtotal) }}</p>
          <p>優惠折扣：−{{ formatTwd(selectedShippingOption.amounts.itemDiscountTotal) }}</p>
          <p>配送費：{{ formatTwd(selectedShippingOption.amounts.shippingFee) }}</p>
          <p>組裝費：{{ formatTwd(selectedShippingOption.amounts.assemblyFee) }}</p>
          <p>
            <strong>應付總額：{{ formatTwd(selectedShippingOption.amounts.grandTotal) }}</strong>
          </p>
        </template>
        <p v-else>
          請先選擇配送方式，以取得即時應付金額。
        </p>
        <p>
          送出訂單時會再次確認優惠、金額與庫存；若有變動，會提醒您重新確認。
        </p>
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
    <dialog
      ref="policyDialog"
      class="checkout-page__policy-dialog"
      :aria-label="policyNames[readingPolicy]"
    >
      <button
        type="button"
        autofocus
        data-test="close-policy"
        @click="policyDialog?.close()"
      >
        關閉政策內容
      </button>
      <p>結帳同意版本：{{ policyVersions?.[readingPolicy] }}；以下為 Demo 引導範例，非正式營運條文。</p>
      <LegalDemoPage
        :kind="readingPolicy"
        embedded
      />
    </dialog>
  </section>
</template>

<style scoped>
.checkout-page__field-error { grid-column: 1 / -1; margin: 0; color: #b91c1c; }

.checkout-page {
  max-width: 52rem;
}

.checkout-page__policy-dialog {
  box-sizing: border-box;
  width: min(56rem, calc(100vw - 2rem));
  max-height: 85dvh;
  padding: 1rem;
  overflow-y: auto;
  border: 1px solid var(--color-border-soft);
  border-radius: 1rem;
  background: #fffdf8;
  color: var(--color-text);
}
.checkout-page__policy-dialog::backdrop { background: #102f3c99; }

.checkout-page__submit {
  display: flex;
  flex-direction: column;
  gap: var(--space-6);
}

.checkout-page__submit > section,
.checkout-page__summary,
.checkout-page__guest-success,
.checkout-page__blocked {
  padding: 1rem;
  border: 1px solid var(--color-border-soft);
  border-radius: var(--radius-md);
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
  gap: var(--space-3);
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
  border-top: 1px solid var(--color-border);
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
  background: var(--color-surface-strong);
}

.checkout-page__alert {
  padding: 0.75rem 1rem;
  border-radius: var(--radius-md);
  background: var(--color-danger-bg);
  color: var(--color-danger);
}

@media (max-width: 40rem) {
  .checkout-page__fields {
    grid-template-columns: 1fr;
    gap: 0.25rem;
  }
}
</style>
