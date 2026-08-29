<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { reactive, ref } from 'vue'
import { useAdminReviewsQuery, useModerateReviewMutation } from '../../features/reviews/queries'
import type { AdminReview } from '../../features/reviews/types'

const status = ref('pendingReview')
const query = useAdminReviewsQuery(status)
const mutation = useModerateReviewMutation(status)
const reasons = reactive<Record<string, { code: string; note: string }>>({})
const feedback = ref<string | null>(null)

const labels: Record<string, string> = {
  pendingReview: '待審核',
  approved: '已公開',
  rejected: '已退回',
  hidden: '已隱藏',
}

function reasonFor(review: AdminReview) {
  return reasons[review.publicId] ??= { code: '', note: '' }
}

function describeError(error: unknown): string {
  return isApiError(error) ? error.message : '審核操作失敗，請稍後再試。'
}

function moderate(review: AdminReview, action: 'approve' | 'reject' | 'hide' | 'restore'): void {
  const reason = reasonFor(review)
  feedback.value = null
  mutation.mutate({
    id: review.publicId,
    action,
    body: {
      reasonCode: reason.code.trim() || `review_${action}`,
      note: reason.note.trim() || null,
      rowVersion: review.rowVersion,
    },
  }, {
    onSuccess: () => { feedback.value = '審核狀態已更新並留下稽核紀錄。' },
    onError: caught => { feedback.value = describeError(caught) },
  })
}
</script>

<template>
  <section
    class="review-queue"
    aria-labelledby="review-queue-title"
  >
    <header>
      <h1 id="review-queue-title">
        商品評價審核
      </h1>
      <p>審核已驗證購買評價；核准後才會顯示於商品頁。</p>
    </header>
    <label class="review-queue__filter">狀態
      <select v-model="status">
        <option value="pendingReview">待審核</option>
        <option value="approved">已公開</option>
        <option value="rejected">已退回</option>
        <option value="hidden">已隱藏</option>
      </select>
    </label>
    <p
      v-if="feedback"
      role="status"
      class="review-queue__feedback"
    >
      {{ feedback }}
    </p>
    <LoadingState
      v-if="query.isPending.value"
      label="審核佇列載入中"
    />
    <ErrorState
      v-else-if="query.isError.value"
      :description="describeError(query.error.value)"
      @retry="query.refetch()"
    />
    <EmptyState
      v-else-if="query.data.value?.length === 0"
      title="目前沒有評價"
      :description="`${labels[status]}佇列是空的。`"
    />
    <template v-else>
      <article
        v-for="review in query.data.value"
        :key="review.publicId"
        class="review-card"
      >
        <div class="review-card__heading">
          <div><h2>{{ review.productName }}</h2><p>{{ review.skuName }} · {{ Number(review.rating) }} 星</p></div>
          <span>{{ labels[review.status] ?? review.status }}</span>
        </div>
        <h3 v-if="review.title">
          {{ review.title }}
        </h3>
        <p>{{ review.content }}</p>
        <p
          v-if="review.rejectionReason"
          class="review-card__rejection"
        >
          既有退回原因：{{ review.rejectionReason }}
        </p>
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
              width="140"
              height="140"
            >
          </li>
        </ul>
        <div class="review-card__reason">
          <label>原因代碼<input
            v-model="reasonFor(review).code"
            maxlength="64"
            :placeholder="`review_${review.status === 'pendingReview' ? 'approve' : 'action'}`"
          ></label>
          <label>備註（選填）<textarea
            v-model="reasonFor(review).note"
            rows="2"
            maxlength="500"
          /></label>
        </div>
        <div class="review-card__actions">
          <button
            v-if="review.status === 'pendingReview'"
            type="button"
            :disabled="mutation.isPending.value"
            @click="moderate(review, 'approve')"
          >
            核准公開
          </button>
          <button
            v-if="review.status === 'pendingReview'"
            type="button"
            :disabled="mutation.isPending.value || !reasonFor(review).code.trim()"
            @click="moderate(review, 'reject')"
          >
            退回修改
          </button>
          <button
            v-if="review.status === 'approved'"
            type="button"
            :disabled="mutation.isPending.value || !reasonFor(review).code.trim()"
            @click="moderate(review, 'hide')"
          >
            隱藏
          </button>
          <button
            v-if="review.status === 'hidden'"
            type="button"
            :disabled="mutation.isPending.value || !reasonFor(review).code.trim()"
            @click="moderate(review, 'restore')"
          >
            恢復公開
          </button>
        </div>
      </article>
    </template>
  </section>
</template>

<style scoped>
.review-queue { display: grid; gap: 1rem; }
.review-queue__filter { display: grid; gap: .25rem; max-width: 14rem; font-weight: 600; }
.review-queue__filter select, .review-card input, .review-card textarea { padding: .625rem; border: 1px solid #9ca3af; border-radius: .375rem; font: inherit; }
.review-queue__feedback { padding: .75rem; background: #ecfdf5; color: #166534; border-radius: .5rem; }
.review-card { display: grid; gap: .75rem; padding: 1rem; border: 1px solid #d1d5db; border-radius: .75rem; }
.review-card__heading { display: flex; justify-content: space-between; gap: 1rem; }
.review-card__heading h2, .review-card__heading p { margin: 0; }
.review-card__heading span { align-self: start; padding: .2rem .6rem; border-radius: 999px; background: #e5e7eb; }
.review-card__rejection { color: #b91c1c; }
.review-card__images { display: flex; gap: .5rem; padding: 0; list-style: none; }
.review-card__images img { object-fit: cover; border-radius: .375rem; }
.review-card__reason { display: grid; grid-template-columns: minmax(12rem, .5fr) minmax(16rem, 1fr); gap: .75rem; }
.review-card__reason label { display: grid; gap: .25rem; font-weight: 600; }
.review-card__actions { display: flex; flex-wrap: wrap; gap: .5rem; }
</style>
