<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed, reactive, ref } from 'vue'
import {
  useCreateReviewMutation,
  useDeleteReviewImageMutation,
  useEligibleReviewItemsQuery,
  useMyReviewsQuery,
  useSubmitReviewMutation,
  useUpdateReviewMutation,
  useUploadReviewImageMutation,
  useWithdrawReviewMutation,
} from '../../features/reviews/queries'
import { formatReviewDate, reviewStatusLabels } from '../../features/reviews/labels'
import type { MemberReview } from '../../features/reviews/types'

const eligibleQuery = useEligibleReviewItemsQuery()
const reviewsQuery = useMyReviewsQuery()
const createMutation = useCreateReviewMutation()
const updateMutation = useUpdateReviewMutation()
const submitMutation = useSubmitReviewMutation()
const withdrawMutation = useWithdrawReviewMutation()
const uploadMutation = useUploadReviewImageMutation()
const deleteImageMutation = useDeleteReviewImageMutation()

const orderItemPublicId = ref('')
const rating = ref(5)
const title = ref('')
const content = ref('')
const feedback = ref<string | null>(null)
const edit = reactive({ id: '', rating: 5, title: '', content: '' })

const isLoading = computed(() => eligibleQuery.isPending.value || reviewsQuery.isPending.value)
const loadError = computed(() => eligibleQuery.error.value ?? reviewsQuery.error.value)
const isMutating = computed(() => [
  createMutation,
  updateMutation,
  submitMutation,
  withdrawMutation,
  uploadMutation,
  deleteImageMutation,
].some(mutation => mutation.isPending.value))

function describeError(error: unknown): string {
  return isApiError(error) ? error.message : '操作失敗，請稍後再試。'
}

function create(submit: boolean): void {
  feedback.value = null
  createMutation.mutate({
    orderItemPublicId: orderItemPublicId.value,
    rating: rating.value,
    title: title.value.trim() || null,
    content: content.value.trim(),
    submit,
  }, {
    onSuccess: () => {
      orderItemPublicId.value = ''
      title.value = ''
      content.value = ''
      rating.value = 5
      feedback.value = submit ? '評價已送出審核。' : '草稿已儲存。'
    },
    onError: caught => { feedback.value = describeError(caught) },
  })
}

function beginEdit(review: MemberReview): void {
  edit.id = review.publicId
  edit.rating = Number(review.rating)
  edit.title = review.title ?? ''
  edit.content = review.content
  feedback.value = null
}

function saveEdit(review: MemberReview): void {
  updateMutation.mutate({
    id: review.publicId,
    body: {
      rating: edit.rating,
      title: edit.title.trim() || null,
      content: edit.content.trim(),
      rowVersion: review.rowVersion,
    },
  }, {
    onSuccess: () => {
      edit.id = ''
      feedback.value = review.status === 'approved'
        ? '內容已更新並重新送審；舊內容已停止公開。'
        : '評價已更新。'
    },
    onError: caught => { feedback.value = describeError(caught) },
  })
}

function submit(review: MemberReview): void {
  submitMutation.mutate({ id: review.publicId, rowVersion: review.rowVersion }, {
    onSuccess: () => { feedback.value = '評價已送出審核。' },
    onError: caught => { feedback.value = describeError(caught) },
  })
}

function withdraw(review: MemberReview): void {
  withdrawMutation.mutate({ id: review.publicId, rowVersion: review.rowVersion }, {
    onSuccess: () => { feedback.value = '評價已撤回。' },
    onError: caught => { feedback.value = describeError(caught) },
  })
}

function upload(review: MemberReview, event: Event): void {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  uploadMutation.mutate({ id: review.publicId, rowVersion: review.rowVersion, file }, {
    onSuccess: () => { feedback.value = '圖片已安全掃描並上傳。' },
    onError: caught => { feedback.value = describeError(caught) },
  })
  input.value = ''
}

function deleteImage(review: MemberReview, sortOrder: number | string): void {
  deleteImageMutation.mutate({ id: review.publicId, sortOrder: Number(sortOrder), rowVersion: review.rowVersion }, {
    onSuccess: () => { feedback.value = '圖片已移除。' },
    onError: caught => { feedback.value = describeError(caught) },
  })
}
</script>

<template>
  <section
    class="reviews-page"
    aria-labelledby="reviews-title"
  >
    <header>
      <h1 id="reviews-title">
        我的商品評價
      </h1>
      <p>只有已完成訂單的購買品項可以評價；送出後需經客服審核才會公開。</p>
    </header>

    <LoadingState
      v-if="isLoading"
      label="評價資料載入中"
    />
    <ErrorState
      v-else-if="loadError"
      :description="describeError(loadError)"
      @retry="() => { eligibleQuery.refetch(); reviewsQuery.refetch() }"
    />
    <template v-else>
      <p
        v-if="feedback"
        role="status"
        class="reviews-page__feedback"
      >
        {{ feedback }}
      </p>

      <form
        v-if="eligibleQuery.data.value?.some(item => !item.reviewPublicId)"
        class="review-form"
        @submit.prevent="create(true)"
      >
        <h2>撰寫新評價</h2>
        <label>
          已購買品項
          <select
            v-model="orderItemPublicId"
            required
          >
            <option
              value=""
              disabled
            >請選擇</option>
            <option
              v-for="item in eligibleQuery.data.value.filter(candidate => !candidate.reviewPublicId)"
              :key="item.orderItemPublicId"
              :value="item.orderItemPublicId"
            >
              {{ item.productName }}／{{ item.skuName }}（{{ item.skuCode }}）
            </option>
          </select>
        </label>
        <label>評分
          <select v-model.number="rating">
            <option
              v-for="value in [5, 4, 3, 2, 1]"
              :key="value"
              :value="value"
            >{{ value }} 星</option>
          </select>
        </label>
        <label>標題（選填）<input
          v-model="title"
          maxlength="80"
        ></label>
        <label>內容<textarea
          v-model="content"
          maxlength="1000"
          required
          rows="5"
        /></label>
        <div class="review-form__actions">
          <button
            type="button"
            :disabled="isMutating || !orderItemPublicId || !content.trim()"
            @click="create(false)"
          >
            儲存草稿
          </button>
          <button
            type="submit"
            :disabled="isMutating || !orderItemPublicId || !content.trim()"
          >
            送出審核
          </button>
        </div>
      </form>

      <section aria-labelledby="existing-reviews-title">
        <h2 id="existing-reviews-title">
          既有評價
        </h2>
        <EmptyState
          v-if="reviewsQuery.data.value?.length === 0"
          title="目前沒有評價"
          description="完成訂單後，即可在上方撰寫評價。"
        />
        <article
          v-for="review in reviewsQuery.data.value"
          :key="review.publicId"
          class="review-card"
        >
          <div class="review-card__heading">
            <div>
              <h3>{{ review.productName }}</h3>
              <p>{{ review.skuName }} · {{ Number(review.rating) }} 星</p>
            </div>
            <span :class="`review-status review-status--${review.status}`">{{ reviewStatusLabels[review.status] ?? review.status }}</span>
          </div>
          <p
            v-if="review.rejectionReason"
            class="review-card__rejection"
          >
            退回原因：{{ review.rejectionReason }}
          </p>
          <form
            v-if="edit.id === review.publicId"
            class="review-form"
            @submit.prevent="saveEdit(review)"
          >
            <label>評分<select v-model.number="edit.rating"><option
              v-for="value in [5, 4, 3, 2, 1]"
              :key="value"
              :value="value"
            >{{ value }} 星</option></select></label>
            <label>標題（選填）<input
              v-model="edit.title"
              maxlength="80"
            ></label>
            <label>內容<textarea
              v-model="edit.content"
              maxlength="1000"
              required
              rows="4"
            /></label>
            <div class="review-form__actions">
              <button
                type="submit"
                :disabled="isMutating || !edit.content.trim()"
              >
                儲存修改
              </button><button
                type="button"
                @click="edit.id = ''"
              >
                取消
              </button>
            </div>
          </form>
          <template v-else>
            <h4 v-if="review.title">
              {{ review.title }}
            </h4>
            <p>{{ review.content }}</p>
          </template>
          <ul
            v-if="review.images.length"
            class="review-card__images"
          >
            <li
              v-for="image in review.images"
              :key="Number(image.sortOrder)"
            >
              <img
                :src="image.url"
                :alt="image.originalFileName"
                width="120"
                height="120"
              >
              <button
                v-if="review.status !== 'hidden'"
                type="button"
                :disabled="isMutating"
                @click="deleteImage(review, image.sortOrder)"
              >
                移除圖片
              </button>
            </li>
          </ul>
          <div class="review-card__actions">
            <button
              v-if="review.status !== 'hidden'"
              type="button"
              :disabled="isMutating"
              @click="beginEdit(review)"
            >
              編輯
            </button>
            <button
              v-if="review.status === 'draft' || review.status === 'rejected'"
              type="button"
              :disabled="isMutating"
              @click="submit(review)"
            >
              送出審核
            </button>
            <label
              v-if="review.status !== 'hidden' && review.images.length < 3"
              class="review-card__upload"
            >上傳 JPG／PNG
              <input
                type="file"
                accept="image/jpeg,image/png"
                :disabled="isMutating"
                @change="upload(review, $event)"
              >
            </label>
            <button
              v-if="!['approved', 'hidden'].includes(review.status)"
              type="button"
              :disabled="isMutating"
              @click="withdraw(review)"
            >
              撤回
            </button>
          </div>
          <small>最後更新：{{ formatReviewDate(review.updatedAtUtc ?? review.createdAtUtc) }}</small>
        </article>
      </section>
    </template>
  </section>
</template>

<style scoped>
/* grid 軌道預設 min-width: auto，長的 select 選項或評價內文會把整頁推寬；
   夾成 minmax(0, 1fr) 之後 375px 不再產生水平溢位。 */
.reviews-page { display: grid; grid-template-columns: minmax(0, 1fr); gap: 1.5rem; max-width: 52rem; }
.reviews-page__feedback { padding: .75rem; border-radius: .5rem; background: var(--color-success-bg); color: var(--color-primary-dark); }
.review-form, .review-card { display: grid; grid-template-columns: minmax(0, 1fr); gap: .75rem; padding: 1rem; border: 1px solid var(--color-border); border-radius: .75rem; }
.review-form label { display: grid; grid-template-columns: minmax(0, 1fr); gap: .25rem; font-weight: 600; }
/* select 的 min-content 寬度來自最長的選項字串；不夾住就會把整頁推寬 */
.review-form input, .review-form select, .review-form textarea { min-width: 0; max-width: 100%; padding: .625rem; border: 1px solid var(--color-text-faint); border-radius: .375rem; font: inherit; }
.review-form__actions, .review-card__actions { display: flex; flex-wrap: wrap; gap: .5rem; align-items: center; }
.review-card { margin-bottom: 1rem; }
.review-card__heading { display: flex; justify-content: space-between; gap: 1rem; }
.review-card__heading h3, .review-card__heading p { margin: 0; }
.review-card__rejection { color: var(--color-danger); }
.review-status { align-self: start; padding: .2rem .6rem; border-radius: 999px; background: var(--color-border-soft); font-size: .8rem; }
.review-status--approved { background: var(--color-success-bg); color: var(--color-primary-dark); }
.review-status--rejected { background: var(--color-danger-bg); color: var(--color-danger); }
.review-status--pendingReview { background: var(--color-warning-bg); color: var(--color-warning); }
.review-card__images { display: flex; gap: .75rem; padding: 0; list-style: none; }
.review-card__images li { display: grid; gap: .25rem; }
.review-card__images img { object-fit: cover; border-radius: .375rem; }
.review-card__upload { display: inline-flex; gap: .4rem; align-items: center; }
</style>
