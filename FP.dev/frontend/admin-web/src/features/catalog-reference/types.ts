import type { components } from '@doselect/web-shared/api'

/**
 * 分類與商品的挑選項目，直接用產生的型別。
 *
 * 先前這裡是手寫的平行型別，因為當時打的是店面的公開端點、回傳的形狀跟 picker
 * 要的不一樣。現在後端有一個用途限定的端點（`/api/v1/admin/coupons/catalog-options`），
 * 回的就是 picker 需要的欄位，不必再自己組一份。
 */
export type CategoryOption = components['schemas']['CouponCategoryOption']
export type ProductOption = components['schemas']['CouponProductOption']
export type ProductOptionStatus = components['schemas']['ProductOptionStatus']

/** 商品狀態的顯示字串。停售品仍會出現在既有已選清單裡，所以要看得懂。 */
export const productStatusLabels: Record<ProductOptionStatus, string> = {
  draft: '草稿',
  published: '已上架',
  unpublished: '已下架',
  discontinued: '已停售',
}
