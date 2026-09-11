import type { PaymentMethod } from './api'

interface PaymentMethodPresentation {
  label: string
  deadline: string
}

export const PAYMENT_METHOD_ORDER: readonly PaymentMethod[] = [
  'creditCard',
  'linePay',
  'applePay',
  'googlePay',
  'atm',
  'convenienceCode',
  'cashOnDelivery',
]

export const PAYMENT_METHOD_PRESENTATION: Record<PaymentMethod, PaymentMethodPresentation> = {
  creditCard: {
    label: '信用卡',
    deadline: '建立付款後 15 分鐘內；若訂單期限較早，以較早者為準',
  },
  linePay: {
    label: 'LINE Pay',
    deadline: '建立付款後 15 分鐘內；若訂單期限較早，以較早者為準',
  },
  applePay: {
    label: 'Apple Pay',
    deadline: '建立付款後 15 分鐘內；若訂單期限較早，以較早者為準',
  },
  googlePay: {
    label: 'Google Pay',
    deadline: '建立付款後 15 分鐘內；若訂單期限較早，以較早者為準',
  },
  atm: {
    label: 'ATM 虛擬帳號',
    deadline: '建立付款後 3 天內；若訂單期限較早，以較早者為準',
  },
  convenienceCode: {
    label: '超商繳費代碼',
    deadline: '建立付款後 3 天內；若訂單期限較早，以較早者為準',
  },
  cashOnDelivery: {
    label: '貨到付款',
    deadline: '收貨或取貨時',
  },
}

export function sortPaymentMethods(methods: readonly PaymentMethod[]): PaymentMethod[] {
  const allowed = new Set(methods)
  return PAYMENT_METHOD_ORDER.filter(method => allowed.has(method))
}
