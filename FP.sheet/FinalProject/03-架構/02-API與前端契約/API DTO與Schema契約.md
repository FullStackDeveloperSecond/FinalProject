---
文件狀態: 已確認
最後更新: 2026-08-31
追蹤項目:
  - DES-10
  - DES-20
  - DES-22
  - DES-23
  - DES-25
  - REQ-03
---

# API DTO 與 Schema 契約

本頁補足 [[03-架構/02-API與前端契約/API Endpoint目錄]] 中具名 DTO 的欄位與上限。實際 OpenAPI 必須由 ASP.NET Core 產生並經 CI 比對；不得讓前端手寫另一套不同型別。

## 表達規則

- `?` 表示可省略／Null；未標示者必填。
- 所有 PublicId 是小寫 UUID `D` 字串；時間是 UTC ISO 8601；TWD 金額是 JSON number、最多兩位小數。
- Request 未知欄位拒絕；字串 Trim＋NFKC；Enum 傳 camelCase 穩定 Token。
- 一般清單使用 `PageResult<T>{ items:T[], pageNumber:int, pageSize:int, totalCount:int, totalPages:int }`。
- 只有共通規範列出的六類例外使用 `CursorPage<T>{ items:T[], nextCursor:string?, hasMore:boolean }`；Cursor 綁定篩選、排序與授權範圍。
- 可編輯資源 Response 回 `rowVersion` Base64；Update／Command Request 必須原樣帶回。

## 公開商品、搜尋與組裝

| Schema | 精確欄位 |
|---|---|
| `ProductSearchQuery` | `q?:string(0..160)`、`category?:code64`、`brand?:code64`、`minPrice/maxPrice?:money`、`inStock?:bool`、`specs?:SpecFilter[0..12]`、`sort?:relevance/priceAsc/priceDesc/newest`、`pageNumber?:int(1.., default1)`、`pageSize?:int(1..50, default20)`、`locale?:zh-TW/ja-JP/ko-KR` |
| `SpecFilter` | `semanticKey:string(64)`、`operator:eq/gte/lte/in`、`value:string(100) or decimal or bool or string[1..10]`、`unit?:string(16)` |
| `ProductCardDto` | `productPublicId`、`defaultSkuPublicId`、`productCode`、`skuCode`、`name`、`brand{code,name}`、`category{code,name}`、`price{list,sale?,currency}`、`availability:inStock/lowStock/outOfStock`、`primaryImage?:{url,alt,width,height}`、`badges:string[0..5]` |
| `ProductDetailDto` | Card 欄位＋`description`、`tags[]`、`images[0..20]`、`skus:PublicSkuDto[1..100]`、`specificationGroups[]`、`shippingRestrictions[]`、`warrantyMonths?`；不回成本、內部 Id 或草稿內容 |
| `PublicSkuDto` | `publicId`、`skuCode`、`name`、`price`、`availability`、`maxPurchasableQuantity`、`specifications[]`、尺寸／重量公開摘要、`isDefault` |
| `CatalogFilterOptionsQuery` | `category?:code64`、`locale?:enum`；只接受公開且啟用分類 |
| `CatalogFilterOptionsDto` | `categories[]`、`brands[]`、`priceRange{min,max}`、`specificationFilters:{semanticKey,label,valueType,unit?,operators,options?}[]`、`sortOptions[]` |
| `AiProductSearchRequest` | `message:string(1..2000)`、`existingParts:ExistingPartInput[0..12]`、`locale:enum`；`existingParts` 每筆須為已確認的 `catalogSku` 或 `structuredManual`，不得接受 SQL、欄名或會員 Id。搜尋不建立客服 Conversation，單次執行由伺服器產生 `searchPublicId` |
| `AiProductSearchResultDto` | `searchPublicId`、`resultType:clarification/recommendations/noResults/degraded`、`clarifications[]`、`intent.proposedExistingParts[]`、驗證後 `recommendations[]`、`customBuild?`、`degradationMode`、`usage`；自然語言解析的既有零件只能作為未確認候選，推薦理由只引用後端候選與相容性結果 |
| `AiCustomBuildRecommendationDto` | `components:AiCustomBuildComponentDto[]`、`purchaseSubtotal`、`assemblyFee=300`、`purchaseTotal`、`currency=TWD`、`compatibilityStatus:compatible/warning`、`compatibilityMessageKeys[]`；成功時涵蓋八類完整清單，`purchaseTotal = purchaseSubtotal + assemblyFee` |
| `AiCustomBuildComponentDto` | `product?:ProductCardDto`、`skuPublicId?`、`sourceType:catalogSku/structuredManual`、`categoryCode`、`displayName`、`quantity`、`isExistingPart`、`reason?`；既有零件 `isExistingPart=true`、不計入新購小計且 `reason=null`，但仍參與相容性 |
| `CompatibilityCheckRequest` | `items:BuildItemInput[1..20]`；公開版不接受 Draft Settings |
| `CompatibilityCheckDto` | `overall:compatible/warning/blocked/insufficientData`、`ruleSetVersion`、`settingsVersion`、`results[]`、`evaluatedAtUtc` |
| `CreateBuildListRequest` | `name:string(1..160)`、`items:BuildItemInput[1..20]` |
| `UpdateBuildListRequest` | Create 欄位＋`rowVersion` |
| `BuildItemInput` | `skuPublicId`、`quantity:int(1..8)` |
| `BuildItemDto` | `publicId`、`skuPublicId`、`skuCode`、`name`、`categoryCode`、`quantity`、`sortOrder`、`unitPrice`、`lineTotal`、`availability` |
| `BuildListDto` | `publicId`、`name`、`items:BuildItemDto[]`、`compatibility{overall,ruleSetVersion,settingsVersion,results[]}`、`totals{merchandise,assemblyFee,grandTotal,currency}`、`activeShare?:BuildActiveShareDto`、`updatedAtUtc`、`rowVersion`；擁有權由後端 Actor Scope／Owner Query 驗證，不回傳固定 Owner 欄位 |
| `BuildActiveShareDto` | `sharePublicId`、`expiresAtUtc?`；只在目前有作用中分享時出現。分享 Token 只存雜湊（不可逆），因此這裡永遠不含可開啟的網址——既有分享的網址只在 `BuildShareDto`（建立／重新產生當下）出現過一次，重新整理後無法再取得，只能撤銷或重新產生 |
| `BuildListSummaryDto` | `publicId`、`name`、`itemCount`、`compatibilityOverall`、`grandTotal`、`isShared`、`updatedAtUtc`、`rowVersion` |
| `BuildShareDto` | `sharePublicId`、`url`、`expiresAtUtc`；`url` 是完整前台網址（`FrontendLinkOptions.BaseUrl` + `/builds/shared/{token}`），不是內部 API 路徑 |
| `SharedBuildDto` | `sharePublicId`、`name`、去識別化 `items`、目前價格／庫存／相容性結果、`canCopy`、`canAddToCart`；不回 Owner |
| `AddBuildToCartRequest` | `quantity:int(1..8)`、`buildRowVersion`；分享清單加入前由後端建立會員複本或購物車群組，不修改原清單 |

相容性 Request／Response 使用 [[03-架構/07-領域設計/相容性規則後台設計]] 的 Item、Overall 與 Result 結構；公開版不接受 Draft Settings，且不回管理設定值。AI 商品搜尋 Schema、`existingParts` Union、上限與 Result Type 使用 [[03-架構/06-AI設計/AI應用詳細設計]]。

## 驗證與帳號

| Schema | 精確欄位／回應 |
|---|---|
| `RegisterRequest` | `email:string(3..320)`、`password:string(12..128)`、`displayName:string(1..100)`、`locale?:enum`、`acceptTermsVersion:int` |
| `EmailVerificationRequest` | `email:string(3..320)`；永遠回 202，不揭露帳號 |
| `EmailVerificationConfirmRequest` | `userPublicId`、`token:string(1..2048)` |
| `LoginRequest` | `email:string(3..320)`、`password:string(1..128)`、`rememberMe:bool` |
| `PasswordResetRequest` | `email:string(3..320)`；永遠回 202 |
| `PasswordResetConfirmRequest` | `userPublicId`、`token:string(1..2048)`、`newPassword:string(12..128)` |
| `AdminLoginRequest` | `email`、`password`；回 `requiresTwoFactor`、`twoFactorChallengePublicId?` |
| `TotpVerifyRequest` | `challengePublicId`、`code:string(6)`；Recovery Code 使用獨立 `code:string(8..64)` |
| `GuestOrderAccessRequest` | `orderNumber:string(1..32)`、`email:string(3..320)`；永遠回 202 |
| `GuestOrderAccessRequestAcceptedDto` | `requestPublicId`、`expiresAtUtc`、`resendAvailableAtUtc`；有效／無效輸入回相同 Schema，不表示訂單或 Email 是否存在 |
| `GuestOrderAccessVerification` | `requestPublicId`、`code:string(6)`；成功回限單、可撤銷、30 分鐘內可多次使用的 HttpOnly Cookie 與 `expiresAtUtc`；Challenge 本身單次使用 |
| `CurrentUserDto` | `publicId`、`displayName`、`emailMasked`、`emailVerified`、`locale`、`roles?:string[]`（只在管理端） |
| `AuthSessionDto` | `isAuthenticated`、`user?:CurrentUserDto`、`expiresAtUtc?`、`requiresTwoFactor?:bool`；管理端只有完成 2FA 才回 Roles／Policies |
| `MemberProfileDto` | `publicId`、`displayName`、`emailMasked`、`emailVerified`、`phone?`、`locale`、`createdAtUtc`、`rowVersion` |
| `UpdateMemberProfileRequest` | `displayName:string(1..100)`、`phone?:string(6..32)`、`locale:enum`、`rowVersion`；不可用此 DTO 修改 Email 或角色 |
| `CreateMemberAddressRequest` | `label:string(1..50)`、`recipientName:string(1..100)`、`phone:string(6..32)`、`postalCode:string(1..16)`、`city:string(1..50)`、`district:string(1..50)`、`addressLine1:string(1..300)`、`addressLine2?:string(0..300)`、`isDefault:bool` |
| `UpdateMemberAddressRequest` | Create 欄位＋`rowVersion` |
| `MemberAddressDto` | Request 欄位＋`publicId`、`createdAtUtc`、`updatedAtUtc`、`rowVersion` |
| `NotificationDto` | `publicId`、`type`、`title`、`message`、`target{type,publicId?,route?}`、`createdAtUtc`、`readAtUtc?`；Route 只能由後端白名單產生 |

密碼、Token、TOTP、Recovery Code 不得出現在 Response、Log、Audit 差異或 OpenAPI Example。

## Cart 與 Checkout

| Schema | 精確欄位 |
|---|---|
| `CartDto` | `publicId`、`items:CartItemDto[0..100]`、`coupon?:CouponAppliedDto`、`amounts{subtotal,itemDiscount,couponDiscount,shippingEstimate?,assemblyFee,totalEstimate,currency}`、`warnings:CartWarningDto[]`、`rowVersion` |
| `CartItemDto` | `publicId`、`skuPublicId`、`skuCode`、`name`、`quantity`、`unitPrice`、`lineTotal`、`availability`、`priceChanged`、`maxPurchasableQuantity`、`assemblyGroupKey?`、`rowVersion` |
| `CartWarningDto` | `code`、`message`；`message` 為後端撰寫的人類可讀說明，前端直接顯示，不得改以 `code` 自行對照 |
| `CartValidationDto` | `cart:CartDto`、`isCheckoutReady`、`issues:{itemPublicId?,code,severity,availableActions[]}[]`、`validatedAtUtc`；`availableActions` 值域 `reduce-quantity`／`remove`／`remove-group`，屬於 AssemblyGroupKey 的品項一律只回 `["remove-group"]`（另兩者會被 `cart_assembly_item_immutable` 拒絕，不得回報） |
| `AddCartItemRequest` | `skuPublicId`、`quantity:int(1..99)`、`cartRowVersion?`；價格、庫存與費用不接受前端輸入 |
| `UpdateCartItemRequest` | `quantity:int(1..99)`、`itemRowVersion`、`cartRowVersion` |
| `RemoveCartItemRequest` | `itemRowVersion`；本專案慣例為 DELETE 亦以 Body 帶 RowVersion，與其他寫入端點一致 |
| `RemoveAssemblyGroupRequest` | `cartRowVersion`；用 Cart 層級（非 Item）RowVersion，因為一個組裝群組橫跨多筆 CartItem，沒有單一 item RowVersion 可代表整組（AUTO-DEC-015） |
| `CartMergeRequest` | `guestCartKey:string(32..256)`、`strategy:mergeAndReportConflicts`、`idempotencyKey:string(8..128)` |
| `CartMergeResultDto` | `cart:CartDto`、`conflicts:{guestItemPublicId,skuPublicId,reason,acceptedQuantity}[]` |
| `ApplyCouponRequest` | `code:string(1..64)`、`cartRowVersion`（8-byte SQL Server RowVersion） |
| `CouponAppliedDto` | `code`、`discountAmount`、`isFreeShipping`、`isAssemblyFreeShipping`；免運券的 `discountAmount` 固定為 0，資格由兩個旗標表達，不得把成功結果靜默呈現為 0 元折扣（DEC B1） |
| `CreateOrderRequest` | `cartPublicId`、`cartRowVersion`、`buyer:{email,name,phone}`、`shipping:{methodCode,address?:AddressInput,storePublicId?:uuid}`、`paymentMethod:enum`、`invoice:{type:simulated,carrier?:string(64)}`、`acceptPolicyVersions:{terms,return,privacy}` |
| `AddressInput` | `recipientName:string(1..100)`、`phone:string(6..32)`、`postalCode?:string(1..16)`、`city?:string(1..50)`、`district?:string(1..50)`、`addressLine1?:string(1..300)`、`addressLine2?:string(0..300)`；宅配時地址欄全部必填，超取不得用地址取代 storePublicId；不接受地址簿 Label |
| `OrderDto` | `publicId`、`orderNumber`、五個狀態、`items:OrderItemDto[]`、收件遮蔽摘要、物流摘要、付款摘要、`amounts`、`paymentDueAtUtc?`、合法 `availableActions:string[]`、各事件時間、`rowVersion` |
| `ShippingOptionsDto` | `cartPublicId`、`options:{methodCode,name,fee,isEligible,ineligibleReasonCode?,freeShippingThreshold?,requiresAddress,requiresStore,allowedPaymentMethods[]}[]`、`evaluatedAtUtc`、`cartRowVersion` |
| `ConvenienceStoreQuery` | `providerCode?:string(64)`、`city?:string(50)`、`district?:string(50)`、`q?:string(100)`、`pageNumber/pageSize` |
| `ConvenienceStoreOptionDto` | `publicId`、`providerCode`、`storeCode`、`name`、`city`、`district`、`address`、`isDemoData:true` |
| `OrderQuery` | `status?:enum[0..10]`、`fromDate/toDate?:YYYY-MM-DD`、`pageNumber/pageSize`；只查目前會員自己的訂單 |
| `OrderSummaryDto` | `publicId`、`orderNumber`、主要摘要狀態與徽章、`itemCount`、`total`、`currency`、配送／付款摘要、`createdAtUtc`、`availableActions[]` |
| `CancelOrderRequest` | `reasonCode:changed_mind/ordered_by_mistake/found_better_price/shipping_too_slow/other`、`note?:string(0..500)`、`orderRowVersion`；清單外原因拒絕，Note 只作為中央 Audit 的選填補充 |

`POST /orders` 的 `Idempotency-Key` Header 必填；Body 不重複保存 Key。宅配需 Address，超取需 Store PublicId；符合 NT$20,000 上限且未含組裝電腦或限制品時，一般宅配與超取均可使用 COD，組裝電腦宅配必須先付款。所有可信折扣與費用完成並整數化後，`GrandTotal` 必須至少 NT$1；低於時回 `409 order_total_below_minimum`，成功 Response／PaymentAttempt 不會建立。

## Payment、Return 與 Refund

| Schema | 精確欄位 |
|---|---|
| `CreatePaymentAttemptRequest` | `method:enum`、`orderRowVersion`；金額由後端訂單決定 |
| `PaymentAttemptDto` | `publicId`、`method`、`status`、`amount`、`currency`、`instruction?:{type,maskedAccount?,code?,expiresAtUtc?}`、`createdAtUtc`、`paidAtUtc?`、`rowVersion` |
| `CompleteSimulatedPaymentRequest` | `outcome:succeeded/failed/cancelled/expired`、`simulationKey:string(8..128)`；展示端點只在 Demo Profile 且 `Demo:SimulationEndpointsEnabled=true` 開放；`simulationKey` 同時作為此命令的唯一重播鍵與不可重複模擬 Provider Event ID，不另使用 `Idempotency-Key` Header；COD 不接受此 Request，必須由 Delivered／PickedUp 收款事件完成 |
| `CreateReturnRequest` | `items:{orderItemPublicId,quantity,reasonCode,description?:string(0..500)}[1..20]`、`requestReason:string(1..1000)`、`orderRowVersion` |
| `ReturnRequestDto` | `publicId`、`orderPublicId`、`status`、`items[]`、`attachments[]`、`requestedAtUtc`、審核／收貨／結案時間、`availableActions[]`、`rowVersion` |
| `ApproveReturnRequest` | `decision:approved/rejected`、`items:{returnItemPublicId,approvedQuantity:int(0..requestedQuantity),inspectionRequired:bool}[1..20]`、`reasonCode:string(1..64)`、`note?:string(0..500)`、`assemblyFeeDisposition?:enum`、`returnShippingCost?:decimal(18,2)>=0`、`returnRowVersion`；核准且不需寄回／驗收時兩個退款快照欄位成對必填，需寄回或拒絕時不得提供 |
| `InspectReturnRequest` | `items:{returnItemPublicId,conditionCode,disposition,note?}[1..20]`、`assemblyFeeDisposition:enum`、`returnShippingCost:decimal(18,2)>=0`、`returnRowVersion`；完成驗收並進入 `AwaitingRefund` 前兩個退款快照欄位成對必填 |
| `ExecuteRefundRequest` | `reasonCode:string(1..64)`、`note?:string(0..1000)`、`refundRowVersion`；`Idempotency-Key` 使用 Header；不接受 `allocations` |
| `RefundAllocationDto` | `orderItemPublicId?`、`quantity?`、`type:itemRefund/originalShipping/returnShipping/assemblyFee/discountClawback/shippingClawback/otherAdjustment`、`amount`；Amount 一律為正值，V1 新寫入禁止 `otherAdjustment`；`itemRefund` 必須有 OrderItem 與正整數 Quantity，其他類型兩欄皆為 Null |
| `MaskedAdminSummaryDto` | `publicId`、`maskedLabel`；只回管理員 PublicId，不回 Internal Identity ID；Label 優先使用遮蔽 DisplayName，缺少時使用遮蔽 Email |
| `RefundDto` | `publicId`、`refundNumber`、`orderPublicId`、`returnPublicId?`、`status`、`requested/approved/succeededAmount`、`allocations:RefundAllocationDto[]`、`requestedBy/approvedBy/executedBy:MaskedAdminSummaryDto?`、時間、`rowVersion` |
| `AdminReturnQuery` | `statuses?`、`reasonCodes?`、`from/to?`、`q?`、`pageNumber/pageSize` |
| `AdminReturnSummaryDto` | PublicId、案件編號、Order 摘要、狀態、品項數、申請時間、寄回期限、注意旗標、RowVersion |
| `AdminReturnDetailDto` | `ReturnRequestDto`＋可授權訂單摘要、檢查結果、可退款分攤預覽、內部歷程及 `availableActions[]` |
| `AdminRefundQuery` | `statuses?`、`from/to?`、`q?`、`pageNumber/pageSize` |
| `ReturnProcessActionRequest` | `action` 對應 `receive/inspect/extend-shipment-deadline` 的具名 oneOf Payload；皆含理由與 RowVersion，延長另含新期限 |
| `CreateReturnShipmentRequest` | `shippingMethodPublicId`、`pickupAddress?` 或 `store/selfShip` 具名 oneOf、`returnRowVersion`；宅配取件地址不得由後端強制覆寫成原訂單地址 |
| `ReturnShipmentDto` | PublicId、ReturnPublicId、Method／Provider 摘要、ShipmentNumber、Status、TrackingNumber?、遮蔽取件／門市快照、事件摘要、時間、RowVersion |
| `AppendReturnShipmentEventRequest` | `source:string(1..32)`、`externalEventId:string(1..128)`、`eventType:string(1..64)`、`occurredAtUtc`、安全摘要；Source＋ExternalEventId 冪等 |
| `CouponDto` | PublicId、Code、Name、Type、Status、期間、門檻、折扣／上限、使用量、適用／排除範圍、是否排除特價、RowVersion |
| `CreateCouponRequest` | Code、Name、Type、期間、規則、總量／每人限制、Scope 與排除項目；百分比必填最大折抵 |
| `UpdateCouponRequest` | Create 欄位＋RowVersion；已產生 Redemption 後不可改寫 Code 或歷史快照 |
| `CouponActionRequest` | `reasonCode`、`note?`、`rowVersion`；Action 只接受 activate／pause／disable |

退貨核准使用 `Return.Approve`（OrderManager／SuperAdmin）；退款金額核定與執行使用 `Refund.Execute`（FinanceManager／SuperAdmin）。兩者採不同 Policy；皆需合法狀態、RowVersion、理由與 Audit，退款另需 Idempotency-Key。

退款分攤以後端 `RefundCalculator` 與已核准 Refund／可信交易快照為唯一權威來源，管理端不得指定會計分攤。退款執行的 `reasonCode` 與經白名單處理的 `note` 只寫入中央 Audit，不在 Refund 重複保存；Audit、退款狀態、分攤與冪等完成紀錄必須在同一 SQL Server 交易提交。遮蔽管理摘要不得包含完整姓名、完整 Email 或內部 Identity ID。

## Simulated Invoice 與 Allowance

| Schema | 精確欄位 |
|---|---|
| `IssueSimulatedInvoiceRequest` | `orderRowVersion`；發票買受人、品項及金額只讀取訂單交易快照；`Idempotency-Key` 使用 Header |
| `SimulatedInvoiceItemDto` | `publicId`、`orderItemPublicId?`、`kind:merchandise\|shipping\|assemblyFee`、商品／SKU 顯示快照、`quantity`、`unitPrice`、`discountAmount`、`netAmount`、`taxAmount`、`grossAmount` |
| `SimulatedInvoiceAllowanceDto` | `publicId`、`allowanceNumber`、`invoicePublicId`、`refundPublicId`、`netAmount`、`taxAmount`、`grossAmount`、`items[]`、`issuedAtUtc`、`demoMarker` |
| `SimulatedInvoiceDto` | `publicId`、`invoiceNumber`、`orderPublicId`、`status`、遮蔽買受人摘要、`netAmount`、`taxAmount`、`grossAmount`、`currency:TWD`、`taxRate:0.05`、`items[]`、`allowances[]`、開立／作廢時間、`demoMarker`、`rowVersion` |
| `AdminInvoiceQuery` | `statuses?`、`from/to?`、`q?`、`pageNumber/pageSize`；一般頁碼分頁 |
| `AdminInvoiceSummaryDto` | PublicId、發票號碼、Order 摘要、Status、未稅／稅額／含稅、開立時間、DemoMarker、RowVersion；不回完整個資 |
| `AdminInvoiceDto` | `SimulatedInvoiceDto`＋管理歷程摘要、`availableActions[]`；完整個資仍需 `PersonalData.ViewFull`，不得因 FinanceManager 身分直接回傳 |
| `VoidSimulatedInvoiceRequest` | `reasonCode:string(1..64)`、`note?:string(0..1000)`、`rowVersion` |
| `CreateSimulatedInvoiceAllowanceRequest` | `refundPublicId`、`invoiceRowVersion`；金額由後端成功 Refund 及原發票明細推導，不接受客戶端金額；`Idempotency-Key` 使用 Header |

折讓 Reader 依 DEC-P298 將成功 Refund 的七類分攤映射到同一張原發票：`ItemRefund` 建立折讓明細；`OriginalShipping`／`AssemblyFee` 只在原發票有對應收費且本次退還時建立；`ReturnShipping` 不建立；`DiscountClawback`／`ShippingClawback` 只作退款扣回，不建立獨立負值折讓明細；`OtherAdjustment` 第一版拒絕。折讓累計數量與金額不得超過原發票各明細可折讓上限。

依 DEC-P299，`kind` 由 Domain 中央常數與交易快照映射：有 `OrderItemId` 的一般 SKU 為 `merchandise`；無 `OrderItemId` 且 `SkuCodeSnapshot` 分別為 `__INVOICE_SHIPPING__`／`__INVOICE_ASSEMBLY_FEE__` 時映射為 `shipping`／`assemblyFee`。保留值不得作為 API 列舉值公開；未知非商品值與 `OtherAdjustment` 必須拒絕，不得靜默過濾。

模擬發票固定 `taxRate = 0.05`。表頭以明細含稅加總四捨五入至 TWD 整數元，`netAmount = Round(issuedAmount / 1.05, 0, AwayFromZero)`、`taxAmount = issuedAmount - netAmount`；表頭 Gross／Net／Tax 均為整數。發票明細可保留兩位小數，每筆維持 `gross = net + tax` 且不得為負；核對採 `Round(Sum(line.gross), 0) = header.issued`、`Round(Sum(line.net), 0) = header.net`、`Sum(line.tax) = header.tax`，最後一筆合法明細吸收稅額尾差。`issuedAmount` 必須等於付款前已整數化的訂單實付金額，不一致時拒絕；例如 NT$1,000 固定為未稅 952、稅額 48。折讓取位仍依 DEC-P280，未由 DEC-P285 覆寫。

## 後台型錄、庫存與物流

| Schema | 精確欄位 |
|---|---|
| `CreateSkuRequest` | `skuCode:string(1..64)`、`nameZhTw:string(1..160)`、`listPrice/unitCost:money`、尺寸／重量、`status`、`isDefault`、`requiresPrepayment:bool`、`specifications:SpecValueInput[0..100]`；特價不得內嵌，改走 SalePrice 契約 |
| `UpdateSkuRequest` | Create 欄位但 `skuCode` 不可改；加 `rowVersion` |
| `SpecValueInput` | `semanticKey:string(1..64)`、`valueType:enum`、四值欄 oneOf |
| `SkuDto` | PublicId、SkuCode、Product 摘要、全部可編輯欄位、Spec DTO、庫存摘要、時間、RowVersion；非 Finance/Catalog 不回 UnitCost |
| `AdminProductQuery` | `q?`、`brandCodes?`、`categoryCodes?`、`statuses?`、`stockState?`、`sort?`、`pageNumber/pageSize`；排序與篩選使用白名單 |
| `AdminProductSummaryDto` | Product PublicId／Code／名稱、品牌、分類、狀態、SKU 數、價格區間、加總庫存、主要圖片、更新時間、RowVersion |
| `CreateProductRequest` | `productCode:string(1..64)`、`nameZhTw:string(1..160)`、`brandPublicId`、`categoryPublicId`、`descriptionZhTw?:string(0..4000)`、`warrantyMonths?:int(0..120)`、`tagPublicIds:uuid[0..20]`、`status:draft/published/unpublished/discontinued`、必填 `defaultSku:CreateSkuRequest`；服務端固定 `defaultSku.isDefault=true`，Product、Tags 與第一個預設 SKU 必須同交易全部成功或全部回滾 |
| `UpdateProductRequest` | Create 欄位但 Product Code 不可改；加 `rowVersion` |
| `AdminProductDetailDto` | Product 全部可編輯欄位、`skus:SkuDto[]`、`images[]`、規格範本摘要、稽核時間及 RowVersion |
| `BulkProductActionRequest` | `productPublicIds:uuid[1..100]`、`rowVersions:{productPublicId,rowVersion}[]`；`adjust-price` 另帶受控調價模式與值、原因 |
| `CatalogLookupDto` | `publicId`、`code`、`nameZhTw`、`isActive`、`sortOrder`、`rowVersion`；Brand／Category／Tag 使用各自具名 Schema |
| `SpecificationDefinitionDto` | `publicId`、`categoryPublicId`、`semanticKey`、`displayNameZhTw`、`valueType`、`unitCode?`、`isRequired`、`isFilterable`、`isProtected`、`isActive`、`sortOrder`、Options、RowVersion |
| `ReleaseReservationRequest` | `reasonCode:enum`、`note:string(1..500)`、`rowVersion` |
| `PackageLimitVersionRequest` | Weight／三邊／總長／申報價正數、`effectiveFromUtc`、`effectiveToUtc?`、`rowVersion?` |
| `ConvenienceStoreRequest` | `providerCode:string(1..64)`、`storeCode:string(1..64)`、`name:string(1..160)`、`address:string(1..500)`、`isActive`、`rowVersion?` |
| `ConvenienceStoreDto` | Request 欄位＋PublicId、縣市／行政區、是否展示資料、建立／更新時間、RowVersion |
| `AdminOrderSummaryDto` | PublicId、OrderNumber、五狀態、BuyerType、遮蔽買家、金額、ShippingMethod、Created／Paid／Shipped／Delivered／Completed 時間、SLA／異常旗標、RowVersion |
| `AdminOrderDto` | Summary＋Order Item／付款／物流／組裝／退貨退款摘要、狀態歷程、遮蔽買家資料、`availableActions[]`；完整收件資料不內嵌 |
| `OrderRecipientDto` | OrderPublicId、RecipientName、Phone、Email、PostalCode、Address、Store Snapshot、`accessPurpose`；每次讀取稽核 |
| `BatchShipmentRequest` | `orders:{orderPublicId,rowVersion}[1..100]`、`shippingAction:createLabel/markShipped`、`idempotencyKey` |
| `BatchShipmentResultDto` | BatchPublicId、Total／Succeeded／Failed、`items:{orderPublicId,status,trackingNumber?,errorCode?}[]`、建立時間 |
| `RetryOutboxMessageRequest` | `reasonCode:string(1..64)`；ASCII 穩定碼，只允許字母、數字、`.`、`_`、`:`、`-`，並須通過中央 Audit 敏感詞拒絕規則 |
| `RetryOutboxMessageResponse` | `publicId`、`status:Pending`、`availableAtUtc`；HTTP 202 |
| `InventoryBalanceQuery` | `q?`、`stockState?`、`categoryCode?`、`pageNumber/pageSize` |
| `InventoryBalanceDto` | SKU PublicId／Code／名稱、`onHand`、`reserved`、`available`、`lowStockThreshold`、`rowVersion` |
| `InventoryReservationDto` | PublicId、Order／SKU 摘要、Quantity、Status、ExpiresAtUtc、CreatedAtUtc、合法 `availableActions[]`、RowVersion |
| `InventoryMovementQuery` | `skuPublicId?`、`movementTypes?`、`from/to?`、`pageNumber/pageSize` |
| `InventoryMovementDto` | PublicId、SKU 摘要、Type、Before／Delta／After、ReasonCode、Actor 摘要、Reference Type／PublicId、OccurredAtUtc |
| `ProductImportBatchDto`／`InventoryImportBatchDto` | Batch PublicId、Type、Template Version、Status、建立者摘要、三組來源檔安全顯示名／Hash 是否存在、RowCount、新增／更新／無變更／錯誤統計、NormalizedContentVersion、CorrelationId、ResultSummary、ExpiresAtUtc、RowVersion；庫存匯入第 2／3 組為 Null |
| `ProductImportRowDto`／`InventoryImportRowDto` | Dataset、SourceRowNumber、StableKey、Action、ErrorCodes[]、安全欄位摘要；不回未清理原始公式 |

商品與庫存匯入 DTO、Header、Preview 與 Confirm 見 [[03-架構/03-資料與一致性/匯入暫存與庫存調整設計]]；圖片／附件 DTO 見 [[03-架構/04-安全與檔案/檔案與圖片儲存設計]]。

## AI 客服與人工客服

| Schema | 精確欄位 |
|---|---|
| `AiConsentRequest` | `policyVersion:int`、`locale:enum`、`accepted:must be true` |
| `AiConsentStatusDto` | `state:missing/granted/denied`、後端目前 `policyVersion`、`locale?`、`decidedAtUtc?`；Reload 時以前端查詢結果為準 |
| `AiSupportMessageRequest` | `conversationPublicId?:uuid`、`message:string(1..2000)`、`referencedOrderPublicIds:uuid[0..3]`、`referencedSupportTicketPublicIds:uuid[0..3]`、`locale:enum`；兩組引用不得含 Empty／重複值 |
| `AiSupportAnswerDto` | `conversationPublicId`、`interactionPublicId`、`answer:string(0..4000)`、`citations:{type,label,resourcePublicId?,url?}[0..8]`、`resultCode`、`degradationMode:none/keywordSearch/createSupportTicket`、`disclaimerKey`、`usage{remainingRequests,resetAtUtc}` |
| `AiUsageDto` | 會員本人每日配額：`feature`、`usedRequests`、`requestLimit`、`windowStartUtc/resetAtUtc`；不得回傳 Token、成本或全站預算門檻狀態 |
| `AdminAiUsageReportDto` | `fromUtc/toUtc`、`rows[{feature,model,status,interactionCount,inputTokens,outputTokens,estimatedCostUsd?}]`、`cumulativeCostUsd?`、US$70／90 門檻狀態、`dataAsOfUtc`；成本欄對非 FinanceManager／SuperAdmin 回 Null |
| `CreateSupportTicketRequest` | `category:enum`、`subject:string(1..200)`、`message:string(1..4000)`、`orderPublicId?:uuid` |
| `SupportTicketDto` | PublicId、Category、Subject、Status、Priority、Order 摘要?、Assignee 摘要?、SLA Due／Overdue、`messages:SupportMessageDto[]`（明細端）、附件摘要、AvailableActions、時間、RowVersion |
| `SupportTicketQuery` | `statuses?`、`category?`、`pageNumber/pageSize`；會員端固定限制 Owner |
| `SupportTicketSummaryDto` | PublicId、案件編號、Category、Subject、Status、最近活動、是否等待會員、未讀回覆數、RowVersion |
| `CreateSupportMessageRequest` | `body:string(1..4000)`、`isInternal:false`（前台不可指定 true）、`rowVersion` |
| `CreateInternalNoteRequest` | `body:string(1..4000)`、`rowVersion`；只存在後台 Schema，不得進會員 Response 或 AI Context |
| `SupportTicketActionRequest` | Action 對應 claim／assign／transfer／change-priority／change-status／cancel／reopen 的具名 oneOf Payload；指派、優先級、取消及重開必填理由，皆帶 RowVersion。claim、一般 change-priority、change-status、cancel、reopen 使用 `SupportTicket.Handle`；assign、transfer、優先級覆核／覆寫使用 `SupportTicket.Supervise` |
| `SupportSlaItemDto` | Ticket PublicId／案件編號、Priority、Assignee、Status、FirstResponseDueAtUtc、ResolutionDueAtUtc、使用比例、IsOverdue、LastActivityAtUtc、RowVersion |
| `CaseWorkbenchQuery` | `caseTypes?:support/report/return[1..3]`、`statuses?:string[0..10]`、`priorities?:string[0..4]`、`assigneePublicId?:uuid`、`overdue?:bool`、`cursor?:string(512)`、`pageSize:int(1..100)` |
| `CaseWorkbenchItemDto` | 固定 12 欄：CaseType、CasePublicId、CaseNumber、Title、Status、Priority、RequesterDisplay、AssigneePublicId?、CreatedAtUtc、LastActivityAtUtc、SlaDueAtUtc?、IsOverdue；不得加入 CustomerReplyState、工作台 RowVersion 或 AssignmentState |

`409 support_ticket_assignment_conflict` 僅回 [[03-架構/02-API與前端契約/API共通規範]] 定義的標準 Problem Details（包含 `code`、`traceId`、`correlationId`），不得擴充 `currentAssigneePublicId`、`currentAssigneeDisplayName` 或其他承辦人資料。最新承辦人、RowVersion 與 AvailableActions 只由重新查詢後的 `SupportTicketDto` 提供。

四個 AI 工具的 Request／Result 上限與安全 Union 以 [[03-架構/06-AI設計/AI應用詳細設計]] 為準；工具不是公開 Endpoint。

## 報表

| Schema | 精確欄位 |
|---|---|
| `ReportQuery` | `fromDate/toDate:YYYY-MM-DD`、`timeZone:Asia/Taipei`、`categoryCode?:64`、`brandCode?:64`、`orderStatuses?:enum[0..10]`、`granularity:day/week/month`、`cursor?:string(512)`、`pageSize:int(1..100)`；Cursor 只控制明細列 |
| `ReportResultDto` | `reportKey` 僅接受 `sales-overview/product-abc/period-comparison/inventory-turnover/gross-margin/product-associations/forecast-anomalies`、`title`、`timeBasis`、`timeZone`、`from/to`、`generatedAtUtc`、`asOfUtc`、`summary:{metricKey,value,unit}[]`、`series:{bucket,metrics}[]`、`rows:CursorPage<ReportRowDto>` |
| Export | 與畫面相同 Filter；CSV UTF-8 BOM 或 XLSX；首列含 ReportKey、時間基準、時區、產生時間；文字防公式注入 |

每個 `reportKey` 的 Row Schema 是獨立 OpenAPI Component，不使用無限制 Dictionary 作正式契約。營收以收款日認列、退款以成功退款日沖減；另提供原訂單完成月份分析維度。一般報表依角色矩陣，財務報表限 FinanceManager／SuperAdmin，AI 成本彙總允許 MarketingAnalyst、CustomerServiceSupervisor、SuperAdmin，成本明細限 FinanceManager／SuperAdmin。

## 已確認的核心契約

- 高風險 Policy、報表認列、`existingParts` Schema 與補問發布門檻已由 DEC-P238／P240／P241／P242 定版。
- M 桌面 UI 的支撐 Schema 已納入本頁；新增 Route 不得回傳 Entity、任意 Dictionary 或未授權欄位。
- 實際 ASP.NET Core OpenAPI 文件、TypeScript Client 與 CI Diff 是 Solution 建立後的實作工作，不再視為產品決策。
