<script setup lang="ts">
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useReturnQuery, useUploadReturnAttachmentMutation } from '../../features/returns/queries'
import { formatDateTime, reasonLabels, statusLabels } from '../../features/returns/labels'

const route = useRoute()
const returnId = computed(() => String(route.params.returnId))

const { data: returnRequest, isPending, isError, error, refetch } = useReturnQuery(returnId)
const uploadMutation = useUploadReturnAttachmentMutation(returnId)

const canUpload = computed(() => returnRequest.value?.availableActions.includes('uploadAttachment') ?? false)

async function handleFileChange(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) {
    return
  }

  await uploadMutation.mutateAsync(file)
  input.value = ''
}
</script>

<template>
  <section aria-labelledby="return-detail-title">
    <RouterLink to="/">
      ← 回首頁
    </RouterLink>

    <LoadingState v-if="isPending" />
    <ErrorState
      v-else-if="isError"
      :title="isApiError(error) && error.status === 404 ? '找不到這個退貨申請' : '無法載入退貨申請'"
      :description="isApiError(error) ? error.message : '請稍後再試一次。'"
      :correlation-id="isApiError(error) ? error.correlationId : undefined"
      :trace-id="isApiError(error) ? error.traceId : undefined"
      @retry="refetch()"
    />

    <template v-else-if="returnRequest">
      <h1 id="return-detail-title">
        {{ returnRequest.returnNumber }}
      </h1>
      <p class="return-detail__status">
        {{ statusLabels[returnRequest.status] }}
      </p>

      <dl class="return-detail__summary">
        <div>
          <dt>訂單編號</dt>
          <dd>{{ returnRequest.orderNumber }}</dd>
        </div>
        <div>
          <dt>退貨原因</dt>
          <dd>{{ reasonLabels[returnRequest.reasonCode] ?? returnRequest.reasonCode }}</dd>
        </div>
        <div>
          <dt>申請時間</dt>
          <dd>{{ formatDateTime(returnRequest.requestedAtUtc) }}</dd>
        </div>
        <div v-if="returnRequest.returnShipmentDueAtUtc">
          <dt>寄回期限</dt>
          <dd>
            {{ formatDateTime(returnRequest.returnShipmentDueAtUtc) }}
            <span v-if="returnRequest.shipmentDeadlineExtended">（已延長一次）</span>
          </dd>
        </div>
      </dl>

      <section
        v-if="returnRequest.description"
        aria-labelledby="return-description-title"
      >
        <h2 id="return-description-title">
          申請說明
        </h2>
        <p class="return-detail__description">
          {{ returnRequest.description }}
        </p>
      </section>

      <section aria-labelledby="return-items-title">
        <h2 id="return-items-title">
          退貨品項
        </h2>
        <ul class="return-detail__items">
          <li
            v-for="item in returnRequest.items"
            :key="item.publicId"
          >
            {{ item.productNameSnapshot || item.skuCodeSnapshot }}｜數量 {{ item.quantity }}｜{{ item.inspectionStatus }}
          </li>
        </ul>
      </section>

      <section
        v-if="returnRequest.shipment"
        aria-labelledby="return-shipment-title"
      >
        <h2 id="return-shipment-title">
          寄回資訊
        </h2>
        <dl class="return-detail__summary">
          <div>
            <dt>寄回方式</dt>
            <dd>{{ returnRequest.shipment.method }}</dd>
          </div>
          <div>
            <dt>物流狀態</dt>
            <dd>{{ returnRequest.shipment.status }}</dd>
          </div>
          <div v-if="returnRequest.shipment.trackingNumber">
            <dt>追蹤號碼</dt>
            <dd>{{ returnRequest.shipment.trackingNumber }}</dd>
          </div>
        </dl>
      </section>

      <section aria-labelledby="return-attachments-title">
        <h2 id="return-attachments-title">
          附件
        </h2>
        <EmptyState
          v-if="returnRequest.attachments.length === 0"
          title="尚無附件"
          description="您可以上傳照片作為退貨證據。"
        />
        <ul
          v-else
          class="return-detail__attachments"
        >
          <li
            v-for="attachment in returnRequest.attachments"
            :key="attachment.publicId"
          >
            <a
              :href="`/api/v1/private-attachments/${attachment.publicId}/content`"
              target="_blank"
              rel="noopener"
            >
              {{ attachment.originalFileName }}
            </a>
          </li>
        </ul>

        <div
          v-if="canUpload"
          class="return-detail__upload"
        >
          <label>
            <span>上傳附件（PNG／JPG／PDF，最多 3 個，單檔 10MB）</span>
            <input
              type="file"
              accept=".png,.jpg,.jpeg,.pdf"
              @change="handleFileChange"
            >
          </label>
          <p
            v-if="uploadMutation.isError.value"
            class="return-detail__error"
            role="alert"
          >
            {{ isApiError(uploadMutation.error.value) ? uploadMutation.error.value.message : '上傳失敗，請重新整理後再試一次。' }}
          </p>
        </div>
      </section>
    </template>
  </section>
</template>

<style scoped>
.return-detail__status {
  display: inline-block;
  padding: 0.25rem 0.75rem;
  border-radius: 999px;
  background: #eff6ff;
  color: #1d4ed8;
  font-weight: 700;
}

.return-detail__summary {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
  gap: 1rem;
  margin-block: 1.5rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.75rem;
}

.return-detail__summary dt {
  font-size: 0.75rem;
  color: #6b7280;
}

.return-detail__summary dd {
  margin-inline-start: 0;
  font-weight: 700;
}

.return-detail__items,
.return-detail__attachments {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  padding-left: 1.25rem;
}

.return-detail__description {
  white-space: pre-wrap;
  color: #374151;
}

.return-detail__upload {
  margin-top: 1rem;
}

.return-detail__error {
  color: #b91c1c;
}
</style>
