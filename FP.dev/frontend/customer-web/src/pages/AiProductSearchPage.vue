<script setup lang="ts">
import { ErrorState } from '@doselect/web-shared/components'
import BuildCategorySlotPicker from '../features/builds/components/BuildCategorySlotPicker.vue'
import { CATALOG_CATEGORY_CODES, categoryLabel } from '../features/catalog/categoryLabels'
import { partSpecificationLabels } from '../features/aiProductSearch/partLabels'
import { computed, ref } from 'vue'
import ProductCard from '../features/catalog/components/ProductCard.vue'
import { useAiProductSearchMutation } from '../features/aiProductSearch/queries'
import type { AiExistingPartRequest, AiProposedExistingPart } from '../features/aiProductSearch/types'

const example = '預算五萬元，主要用 Premiere 剪 4K 影片，偶爾玩 3A 遊戲，希望安靜、不要 RGB。'
const message = ref('')
const partsError = ref('')
const accumulatedContext = ref('')
const existingParts = ref<AiExistingPartRequest[]>([])
const confirmedProposalKeys = ref<string[]>([])
const mutation = useAiProductSearchMutation()

const result = computed(() => mutation.data.value)
const remainingRequests = computed(() => Number(result.value?.usage.remainingRequests ?? 0))
const products = computed(() => result.value?.resultType === 'degraded'
  ? result.value.fallbackProducts
  : [])
const proposedExistingParts = computed(() => result.value?.intent?.proposedExistingParts ?? [])
const customBuild = computed(() => result.value?.customBuild ?? null)

function addCatalogPart() {
  if (existingParts.value.length >= 12) return
  existingParts.value.push({
    sourceType: 'catalogSku',
    skuPublicId: '',
    categoryCode: '',
    displayName: '',
    specifications: [],
    quantity: 1,
    confirmedByUser: true,
  })
}

function addManualPart() {
  if (existingParts.value.length >= 12) return
  existingParts.value.push({
    sourceType: 'structuredManual',
    categoryCode: '',
    displayName: '',
    specifications: [{ semanticKey: '', operator: 'eq', value: '', unit: null }],
    quantity: 1,
    confirmedByUser: true,
  })
}

function addSpecification(part: AiExistingPartRequest) {
  if (part.specifications.length >= 12) return
  part.specifications.push({ semanticKey: '', operator: 'eq', value: '', unit: null })
}

function removeSpecification(part: AiExistingPartRequest, index: number) {
  part.specifications.splice(index, 1)
}

function removeExistingPart(index: number) {
  existingParts.value.splice(index, 1)
}

function proposalKey(part: AiProposedExistingPart, index: number): string {
  return `${index}:${part.categoryCode}:${part.displayName}`
}

function confirmProposedPart(part: AiProposedExistingPart, index: number) {
  if (existingParts.value.length >= 12) return
  const key = proposalKey(part, index)
  if (confirmedProposalKeys.value.includes(key)) return
  existingParts.value.push({
    sourceType: 'structuredManual',
    categoryCode: part.categoryCode,
    displayName: part.displayName,
    specifications: part.specifications.length
      ? part.specifications.map(spec => ({ ...spec }))
      : [{ semanticKey: '', operator: 'eq', value: '', unit: null }],
    quantity: part.quantity,
    confirmedByUser: true,
  })
  confirmedProposalKeys.value.push(key)
}

function useExample() {
  message.value = example
}

async function submitSearch() {
  const current = message.value.trim()
  if (!current) return
  const combined = accumulatedContext.value
    ? `${accumulatedContext.value}\n使用者補充：${current}`
    : current
  const validParts = existingParts.value
    .filter(part => part.sourceType === 'catalogSku'
      ? Boolean(part.skuPublicId)
      : Boolean(part.categoryCode && part.displayName && part.specifications.length
        && part.specifications.every(spec => spec.semanticKey && spec.operator && spec.value)))
    .map(part => ({ ...part, quantity: Number(part.quantity ?? 1) }))
  partsError.value = ''
  if (validParts.length !== existingParts.value.length) {
    partsError.value = '請選好站內零件，或填妥手填零件的分類、名稱與規格；也可以移除尚未確認的零件。'
    return
  }
  const response = await mutation.mutateAsync({
    message: combined.slice(0, 2000),
    locale: 'zh-TW',
    existingParts: validParts,
  })

  if (response.resultType === 'clarification') {
    accumulatedContext.value = combined
    message.value = ''
  } else {
    accumulatedContext.value = ''
  }
}

function restart() {
  mutation.reset()
  accumulatedContext.value = ''
  message.value = ''
  confirmedProposalKeys.value = []
}

function formatBudget(value: number | string | null): string {
  return value == null ? '未指定' : `NT$${Number(value).toLocaleString('zh-Hant-TW')}`
}

function formatMoney(value: number | string): string {
  return `NT$${Number(value).toLocaleString('zh-Hant-TW')}`
}
</script>

<template>
  <section
    class="ai-search"
    aria-labelledby="ai-search-title"
  >
    <header class="ai-search__hero">
      <p class="ai-search__eyebrow">
        AI 懂選
      </p>
      <h1 id="ai-search-title">
        說出需求，不必先學會所有規格
      </h1>
      <p class="view-lede">
        告訴我們用途、預算與偏好；AI 只負責理解需求，商品、價格、庫存與相容性仍由系統重新驗證。
      </p>
    </header>

    <form
      class="card ai-search__form"
      @submit.prevent="submitSearch"
    >
      <label class="form-field">
        <span>{{ result?.resultType === 'clarification' ? '補充你的答案' : '你想找什麼？' }}</span>
        <textarea
          v-model="message"
          required
          minlength="1"
          maxlength="2000"
          rows="5"
          placeholder="例如：預算五萬元，想剪 4K 影片，偶爾玩 3A，希望安靜。"
        />
        <small>{{ message.length }} / 2000</small>
      </label>

      <button
        type="button"
        class="ai-search__example"
        @click="useExample"
      >
        帶入展示範例
      </button>

      <p
        v-if="partsError"
        role="alert"
        class="form-error"
      >
        {{ partsError }}
      </p>
      <details class="ai-search__parts">
        <summary>我有既有零件，需要一起檢查相容性</summary>
        <p>搜尋站內商品並選擇規格，或填入手邊零件的資訊。不確定的規格可先在上方描述需求，最多加入 12 項零件。</p>
        <div
          v-for="(part, index) in existingParts"
          :key="index"
          class="ai-search__part-row"
        >
          <div
            v-if="part.sourceType === 'catalogSku'"
            class="ai-search__catalog-part"
          >
            <label>
              零件分類
              <select
                v-model="part.categoryCode"
                @change="part.skuPublicId = ''; part.displayName = ''"
              >
                <option value="">請選擇分類</option>
                <option
                  v-for="code in CATALOG_CATEGORY_CODES"
                  :key="code"
                  :value="code"
                >{{ categoryLabel(code) }}</option>
              </select>
            </label>
            <BuildCategorySlotPicker
              v-if="part.categoryCode"
              :key="part.categoryCode"
              :category-code="part.categoryCode"
              :in-stock-only="false"
              @select="part.skuPublicId = $event.skuPublicId; part.displayName = $event.name"
            />
            <p
              v-if="part.skuPublicId"
              role="status"
            >
              已選擇：{{ part.displayName }}
            </p>
          </div>
          <div
            v-else
            class="ai-search__manual-part"
          >
            <label>
              零件分類
              <select
                v-model="part.categoryCode"
                data-testid="manual-category"
              >
                <option value="">請選擇分類</option>
                <option
                  v-for="code in CATALOG_CATEGORY_CODES"
                  :key="code"
                  :value="code"
                >{{ categoryLabel(code) }}</option>
              </select>
            </label>
            <label>
              顯示名稱
              <input
                v-model="part.displayName"
                data-testid="manual-display-name"
                type="text"
                maxlength="160"
                placeholder="例如：我的 AM5 處理器"
              >
            </label>
            <p>請填入該分類所有相容性必要規格；缺少時系統會補問，不會猜測。</p>
            <div
              v-for="(spec, specIndex) in part.specifications"
              :key="specIndex"
              class="ai-search__spec-row"
            >
              <select
                v-model="spec.semanticKey"
                data-testid="manual-semantic-key"
                aria-label="規格項目"
              >
                <option value="">
                  請選擇規格項目
                </option>
                <option
                  v-if="spec.semanticKey && !partSpecificationLabels[spec.semanticKey]"
                  :value="spec.semanticKey"
                >
                  其他已確認規格
                </option>
                <option
                  v-for="(label, key) in partSpecificationLabels"
                  :key="key"
                  :value="key"
                >
                  {{ label }}
                </option>
              </select>
              <select
                v-model="spec.operator"
                aria-label="規格運算子"
              >
                <option value="eq">
                  等於
                </option>
                <option value="in">
                  包含任一
                </option>
              </select>
              <input
                v-model="spec.value"
                data-testid="manual-spec-value"
                type="text"
                placeholder="規格值；多值以逗號分隔"
                aria-label="規格值"
              >
              <input
                v-model="spec.unit"
                type="text"
                maxlength="16"
                placeholder="單位（可空）"
                aria-label="規格單位"
              >
              <button
                type="button"
                aria-label="移除規格"
                @click="removeSpecification(part, specIndex)"
              >
                移除
              </button>
            </div>
            <button
              type="button"
              :disabled="part.specifications.length >= 12"
              @click="addSpecification(part)"
            >
              加入規格
            </button>
          </div>
          <label>
            數量
            <input
              v-model="part.quantity"
              type="number"
              min="1"
              max="8"
            >
          </label>
          <button
            type="button"
            @click="removeExistingPart(index)"
          >
            移除
          </button>
        </div>
        <div class="ai-search__part-actions">
          <button
            type="button"
            :disabled="existingParts.length >= 12"
            @click="addCatalogPart"
          >
            加入站內 SKU
          </button>
          <button
            type="button"
            :disabled="existingParts.length >= 12"
            @click="addManualPart"
          >
            加入手填零件
          </button>
        </div>
      </details>

      <div class="ai-search__actions">
        <button
          type="submit"
          :disabled="!message.trim() || mutation.isPending.value"
        >
          {{ mutation.isPending.value ? '懂選分析中…' : '開始懂選' }}
        </button>
        <button
          v-if="result"
          type="button"
          @click="restart"
        >
          重新開始
        </button>
      </div>
    </form>

    <ErrorState
      v-if="mutation.isError.value"
      title="AI 懂選目前無法處理"
      description="'你仍可使用一般商品搜尋；稍後也可以再試一次。'"
      @retry="submitSearch"
    />

    <section
      v-if="result?.resultType === 'clarification'"
      class="card ai-search__clarification"
      aria-live="polite"
    >
      <h2>再確認一點資訊</h2>
      <ul>
        <li
          v-for="question in result.clarifications"
          :key="question"
        >
          {{ question }}
        </li>
      </ul>
      <p>回答上方問題後再次送出，我們不會自行猜測必要條件。</p>
    </section>

    <section
      v-if="proposedExistingParts.length"
      class="card ai-search__proposals"
      aria-labelledby="proposed-parts-title"
    >
      <h2 id="proposed-parts-title">
        AI 解析到的既有零件（尚未確認）
      </h2>
      <p>以下只來自你的文字描述，確認前不會參與相容性計算，也不會自動對應成站內 SKU。</p>
      <article
        v-for="(part, index) in proposedExistingParts"
        :key="proposalKey(part, index)"
        class="ai-search__proposal"
      >
        <div>
          <strong>{{ part.displayName }}</strong>
          <span>{{ part.categoryCode }} × {{ part.quantity }}</span>
          <ul v-if="part.specifications.length">
            <li
              v-for="spec in part.specifications"
              :key="`${spec.semanticKey}:${spec.value}`"
            >
              {{ spec.semanticKey }} {{ spec.operator }} {{ spec.value }} {{ spec.unit ?? '' }}
            </li>
          </ul>
          <p v-else>
            尚無可確認的結構化規格，加入後請補齊相容性必要欄位。
          </p>
        </div>
        <button
          type="button"
          :disabled="existingParts.length >= 12 || confirmedProposalKeys.includes(proposalKey(part, index))"
          @click="confirmProposedPart(part, index)"
        >
          {{ confirmedProposalKeys.includes(proposalKey(part, index)) ? '已加入待確認清單' : '確認並加入' }}
        </button>
      </article>
    </section>

    <section
      v-if="result?.intent"
      class="card ai-search__intent"
      aria-labelledby="ai-intent-title"
    >
      <h2 id="ai-intent-title">
        理解到的需求
      </h2>
      <dl>
        <div><dt>類型</dt><dd>{{ result.intent.intent }}</dd></div>
        <div><dt>用途</dt><dd>{{ result.intent.purposes.join('、') || '未指定' }}</dd></div>
        <div><dt>預算</dt><dd>{{ formatBudget(result.intent.minimumBudget) }} ～ {{ formatBudget(result.intent.maximumBudget) }}</dd></div>
        <div><dt>分類</dt><dd>{{ result.intent.categoryCode ?? '未指定' }}</dd></div>
        <div><dt>偏好</dt><dd>{{ result.intent.preferences.join('、') || '未指定' }}</dd></div>
      </dl>
    </section>

    <section
      v-if="result?.resultType === 'recommendations'"
      aria-labelledby="recommendations-title"
    >
      <div class="ai-search__result-heading">
        <div>
          <h2 id="recommendations-title">
            懂選推薦
          </h2>
          <p v-if="customBuild">
            以下完整組裝已由後端確認八類必要零件、價格、可售庫存與確定性相容性。
          </p>
          <p v-else>
            以下商品已由後端重新確認上架、價格與可售庫存。
          </p>
        </div>
        <span>今日剩餘 {{ remainingRequests }} 次</span>
      </div>
      <article
        v-if="customBuild"
        class="card ai-search__custom-build"
      >
        <div class="ai-search__build-summary">
          <div>
            <h3>完整組裝清單</h3>
            <p>既有零件會參與相容性檢查，但不計入本次新購預算。</p>
          </div>
          <dl>
            <div><dt>新購零件</dt><dd>{{ formatMoney(customBuild.purchaseSubtotal) }}</dd></div>
            <div><dt>組裝費</dt><dd>{{ formatMoney(customBuild.assemblyFee) }}</dd></div>
            <div><dt>本次總計</dt><dd>{{ formatMoney(customBuild.purchaseTotal) }}</dd></div>
          </dl>
          <p class="ai-search__compatibility">
            相容性：{{ customBuild.compatibilityStatus === 'Compatible' ? '通過' : '通過但有提醒' }}
          </p>
        </div>
        <div class="ai-search__build-components">
          <article
            v-for="component in customBuild.components"
            :key="`${component.sourceType}:${component.skuPublicId ?? component.categoryCode}:${component.displayName}`"
            class="ai-search__build-component"
          >
            <ProductCard
              v-if="component.product"
              :product="component.product"
            />
            <div v-else>
              <p class="ai-search__component-category">
                {{ component.categoryCode }}
              </p>
              <h4>{{ component.displayName }}</h4>
            </div>
            <div>
              <span
                v-if="component.isExistingPart"
                class="ai-search__existing-badge"
              >既有零件・不計入新購預算</span>
              <p>數量：{{ component.quantity }}</p>
              <p v-if="component.reason">
                {{ component.reason }}
              </p>
            </div>
          </article>
        </div>
      </article>
      <div class="ai-search__recommendations">
        <article
          v-for="item in result.recommendations"
          :key="item.product.defaultSkuPublicId"
          class="card ai-search__recommendation"
        >
          <ProductCard :product="item.product" />
          <div>
            <h3>推薦理由</h3>
            <p>{{ item.reason }}</p>
            <p
              v-if="item.compatibilityStatus !== 'NotRequired'"
              class="ai-search__compatibility"
            >
              相容性：{{ item.compatibilityStatus === 'Compatible' ? '通過' : '通過但有提醒' }}
            </p>
          </div>
        </article>
      </div>
    </section>

    <section
      v-if="result?.resultType === 'noResults'"
      class="card"
      aria-live="polite"
    >
      <h2>目前沒有完全符合的商品</h2>
      <p>可以提高預算、放寬品牌或規格偏好，再重新描述需求。</p>
    </section>

    <section
      v-if="result?.resultType === 'degraded'"
      class="card ai-search__degraded"
      aria-live="polite"
    >
      <h2>AI 暫時無法使用，已改用一般搜尋</h2>
      <p>下列只是關鍵字搜尋結果，不代表 AI 推薦或相容性保證。</p>
      <div
        v-if="products.length"
        class="ai-search__fallback-grid"
      >
        <ProductCard
          v-for="product in products"
          :key="product.defaultSkuPublicId"
          :product="product"
        />
      </div>
      <RouterLink
        v-else
        :to="{ name: 'products', query: { q: message } }"
      >
        前往一般商品搜尋
      </RouterLink>
    </section>

    <p
      v-if="result"
      class="ai-search__disclaimer"
    >
      AI 推薦可能有誤；價格、庫存與相容性請以商品頁及系統檢查結果為準。
    </p>
  </section>
</template>

<style scoped>
.ai-search__catalog-part { display: grid; gap: var(--space-3); min-width: 0; }
.ai-search__catalog-part select, .ai-search__manual-part select { max-width: 100%; min-width: 0; }

.ai-search { display: grid; gap: 1.5rem; }
.ai-search__hero { padding: 1rem 0; }
.ai-search__eyebrow { margin: 0; color: var(--color-primary-dark); font-weight: 800; letter-spacing: .08em; }
.ai-search__form { display: grid; gap: 1rem; }
.ai-search__form textarea, .ai-search__part-row input { width: 100%; min-height: 44px; padding: .75rem; border: 1px solid var(--color-border); border-radius: var(--radius-sm); font: inherit; }
.ai-search__example { justify-self: start; }
.ai-search__parts { border-top: 1px solid var(--color-border-soft); padding-top: 1rem; }
.ai-search__parts summary { cursor: pointer; font-weight: 700; }
.ai-search__part-row { display: grid; grid-template-columns: minmax(16rem, 1fr) 7rem auto; gap: .75rem; align-items: end; margin: .75rem 0; }
.ai-search__manual-part { display: grid; gap: .75rem; }
.ai-search__spec-row { display: grid; grid-template-columns: 1.2fr .7fr 1.2fr .7fr auto; gap: .5rem; align-items: end; }
.ai-search__spec-row select { min-height: 44px; padding: .5rem; border: 1px solid var(--color-border); border-radius: var(--radius-sm); }
.ai-search__part-actions { display: flex; gap: .75rem; flex-wrap: wrap; }
.ai-search__actions, .ai-search__result-heading { display: flex; gap: .75rem; align-items: center; justify-content: space-between; flex-wrap: wrap; }
.ai-search__clarification, .ai-search__intent { display: grid; gap: .5rem; }
.ai-search__proposals { display: grid; gap: .75rem; }
.ai-search__proposal { display: flex; justify-content: space-between; gap: 1rem; align-items: start; padding-top: .75rem; border-top: 1px solid var(--color-border-soft); }
.ai-search__proposal span { display: block; color: var(--color-text-muted); }
.ai-search__intent dl { display: grid; grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr)); gap: 1rem; margin: 0; }
.ai-search__intent dt { color: var(--color-text-muted); font-size: .85rem; }
.ai-search__intent dd { margin: 0; font-weight: 700; }
.ai-search__recommendations { display: grid; gap: 1rem; }
.ai-search__recommendation { display: grid; grid-template-columns: minmax(14rem, 22rem) 1fr; gap: 1.25rem; }
.ai-search__recommendation h3 { margin-top: 0; }
.ai-search__compatibility { color: var(--color-success); font-weight: 700; }
.ai-search__custom-build { display: grid; gap: 1.25rem; }
.ai-search__build-summary { display: grid; gap: .75rem; }
.ai-search__build-summary h3, .ai-search__build-summary p { margin: 0; }
.ai-search__build-summary dl { display: grid; grid-template-columns: repeat(3, minmax(9rem, 1fr)); gap: .75rem; margin: 0; }
.ai-search__build-summary dl > div { padding: .75rem; border-radius: var(--radius-sm); background: var(--color-surface-muted); }
.ai-search__build-summary dt { color: var(--color-text-muted); font-size: .85rem; }
.ai-search__build-summary dd { margin: .25rem 0 0; font-size: 1.1rem; font-weight: 800; }
.ai-search__build-components { display: grid; gap: 1rem; }
.ai-search__build-component { display: grid; grid-template-columns: minmax(14rem, 22rem) 1fr; gap: 1rem; padding-top: 1rem; border-top: 1px solid var(--color-border-soft); }
.ai-search__build-component h4 { margin: .25rem 0; }
.ai-search__component-category { margin: 0; color: var(--color-text-muted); font-size: .85rem; font-weight: 700; }
.ai-search__existing-badge { display: inline-flex; padding: .25rem .55rem; border-radius: 999px; background: var(--color-surface-muted); font-weight: 700; }
.ai-search__fallback-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr)); gap: 1rem; }
.ai-search__disclaimer { color: var(--color-text-muted); font-size: .9rem; }
@media (max-width: 700px) {
  .ai-search__part-row, .ai-search__recommendation, .ai-search__spec-row, .ai-search__build-component { grid-template-columns: 1fr; }
  .ai-search__build-summary dl { grid-template-columns: 1fr; }
  .ai-search__proposal { flex-direction: column; }
}
</style>
