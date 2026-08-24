<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { isApiError } from '@doselect/web-shared/api'
import { LoadingState, ErrorState } from '@doselect/web-shared/components'
import {
  useAdminMemberDetailQuery,
  useResetMemberPasswordMutation,
  useSetMemberAccountStatusMutation,
  useUpdateAdminMemberProfileMutation,
} from '../queries/useAdminMembers'

const route = useRoute()
const router = useRouter()

const publicId = computed(() => String(route.params.publicId ?? ''))
const { data, isPending, isError, error, refetch } = useAdminMemberDetailQuery(publicId)

const updateProfile = useUpdateAdminMemberProfileMutation()
const resetPassword = useResetMemberPasswordMutation()
const setAccountStatus = useSetMemberAccountStatusMutation()

const editing = ref(false)
const editForm = reactive({ displayName: '', birthDate: '' })
const actionMessage = ref('')

watch(data, (value) => {
  if (value) {
    editForm.displayName = value.displayName
    editForm.birthDate = value.birthDate ?? ''
  }
}, { immediate: true })

const statusLabels: Record<string, string> = {
  PendingEmailVerification: '待驗證',
  Active: '啟用',
  Suspended: '停用',
  Anonymized: '已匿名化',
  Disabled: '停用',
}

function statusLabel(status: string): string {
  return statusLabels[status] ?? status
}

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString('zh-TW', { hour12: false })
}

function formatMoney(value: number): string {
  return new Intl.NumberFormat('zh-TW', { style: 'currency', currency: 'TWD', maximumFractionDigits: 0 }).format(value)
}

function startEdit(): void {
  editing.value = true
  actionMessage.value = ''
}

function describeError(caught: unknown): string {
  if (isApiError(caught) && caught.code === 'concurrency_conflict') {
    return '這位會員的資料已被其他人更新，請重新整理後再試一次。'
  }
  return '操作失敗，請稍後再試。'
}

async function saveEdit(): Promise<void> {
  if (!data.value) {
    return
  }
  try {
    await updateProfile.mutateAsync({
      publicId: publicId.value,
      request: {
        displayName: editForm.displayName,
        birthDate: editForm.birthDate || null,
        rowVersion: data.value.rowVersion,
      },
    })
    editing.value = false
    actionMessage.value = '資料已更新。'
  } catch (caught) {
    actionMessage.value = describeError(caught)
  }
}

async function onResetPassword(): Promise<void> {
  if (!globalThis.confirm('確定要寄送密碼重設信給這位會員嗎？')) {
    return
  }
  try {
    await resetPassword.mutateAsync(publicId.value)
    actionMessage.value = '已寄送密碼重設信。'
  } catch (caught) {
    actionMessage.value = describeError(caught)
  }
}

// 後端 Suspend()/Reactivate() 只接受特定的狀態轉換：Reactivate 只能從 Suspended
// 轉換；Anonymized／Disabled 是終止狀態，兩者都不接受。「是否啟用中」不能簡化成
// 「== Active」，PendingEmailVerification 這類狀態也要能被停用。
const terminalStatuses = new Set(['Anonymized', 'Disabled'])

function canToggleStatus(status: string): boolean {
  return !terminalStatuses.has(status)
}

async function onToggleStatus(): Promise<void> {
  if (!data.value || !canToggleStatus(data.value.accountStatus)) {
    return
  }
  const suspend = data.value.accountStatus !== 'Suspended'
  const confirmText = suspend ? '確定要停用這位會員的帳號嗎？' : '確定要重新啟用這位會員的帳號嗎？'
  if (!globalThis.confirm(confirmText)) {
    return
  }
  try {
    await setAccountStatus.mutateAsync({
      publicId: publicId.value,
      request: { suspend, rowVersion: data.value.rowVersion },
    })
    actionMessage.value = suspend ? '帳號已停用。' : '帳號已重新啟用。'
  } catch (caught) {
    actionMessage.value = describeError(caught)
  }
}

function backToList(): void {
  void router.push('/members')
}
</script>

<template>
  <section class="page">
    <button
      type="button"
      class="link-button"
      @click="backToList"
    >
      ← 返回會員列表
    </button>

    <LoadingState v-if="isPending" />
    <ErrorState
      v-else-if="isError"
      :description="error?.message"
      @retry="refetch"
    />

    <template v-else-if="data">
      <header class="member-header">
        <div
          class="member-header__avatar"
          aria-hidden="true"
        >
          {{ data.displayName.slice(0, 1) }}
        </div>
        <div class="member-header__info">
          <h1>{{ data.displayName }}</h1>
          <p>郵件：{{ data.email }}</p>
          <p>加入日期：{{ formatDateTime(data.registeredAtUtc) }}</p>
          <p>
            當前狀態：
            <span
              class="badge"
              :class="data.accountStatus === 'Active' ? 'badge--active' : 'badge--inactive'"
            >
              {{ statusLabel(data.accountStatus) }}
            </span>
          </p>
        </div>
        <div class="member-header__id">
          ID: {{ data.publicId.slice(0, 8) }}
        </div>
        <div class="member-header__actions">
          <button
            type="button"
            @click="startEdit"
          >
            編輯資料
          </button>
          <!--
            ⚠ PENDING ALEX POLICY REVIEW：更改密碼／重設權限對應後端 Member.ManageSensitive
            Policy，是這次新提案、尚未經 alex 核准的權限分級。依團隊決策這裡先把 UI+API
            完整接上，不做成假動作，PR/日誌會標註待覆核。
          -->
          <button
            type="button"
            :disabled="resetPassword.isPending.value"
            @click="onResetPassword"
          >
            更改密碼
          </button>
          <button
            type="button"
            :disabled="setAccountStatus.isPending.value || !canToggleStatus(data.accountStatus)"
            @click="onToggleStatus"
          >
            重設權限
          </button>
        </div>
      </header>

      <p
        v-if="actionMessage"
        class="action-message"
        role="status"
      >
        {{ actionMessage }}
      </p>

      <div class="member-columns">
        <section class="member-column">
          <h2>基本資料</h2>
          <template v-if="!editing">
            <dl class="detail-list">
              <dt>電子郵件</dt>
              <dd>{{ data.email }}</dd>
              <dt>電話</dt>
              <dd>{{ data.phone ?? '未提供' }}</dd>
              <dt>生日</dt>
              <dd>{{ data.birthDate ?? '未提供' }}</dd>
            </dl>
            <!--
              性別、會員等級：資料庫沒有對應欄位、也沒有既有業務規則可換算，
              這裡刻意不顯示，不是漏做（詳見 AdminMemberDetailDto 註解）。
            -->
          </template>
          <form
            v-else
            class="edit-form"
            @submit.prevent="saveEdit"
          >
            <label class="field">
              <span class="field__label">姓名</span>
              <input
                v-model="editForm.displayName"
                type="text"
                required
              >
            </label>
            <label class="field">
              <span class="field__label">生日</span>
              <input
                v-model="editForm.birthDate"
                type="date"
              >
            </label>
            <div class="edit-form__actions">
              <button
                type="submit"
                :disabled="updateProfile.isPending.value"
              >
                儲存
              </button>
              <button
                type="button"
                @click="editing = false"
              >
                取消
              </button>
            </div>
          </form>

          <dl class="detail-list detail-list--stats">
            <dt>總消費金額</dt>
            <dd>{{ formatMoney(data.stats.totalSpend) }}</dd>
            <dt>總訂單數</dt>
            <dd>{{ data.stats.totalOrderCount }}</dd>
            <dt>退貨率</dt>
            <dd>{{ data.stats.returnRatePercent }}%</dd>
          </dl>
        </section>

        <section class="member-column">
          <h2>訂單紀錄</h2>
          <ul
            v-if="data.recentOrders.length > 0"
            class="order-list"
          >
            <li
              v-for="order in data.recentOrders"
              :key="order.orderPublicId"
            >
              <span>{{ formatDateTime(order.placedAtUtc) }}</span>
              <span>{{ order.orderNumber }}</span>
              <span>{{ order.orderStatus }}</span>
              <span>{{ formatMoney(order.grandTotal) }}</span>
            </li>
          </ul>
          <p
            v-else
            class="empty-hint"
          >
            尚無訂單紀錄
          </p>
        </section>

        <section class="member-column">
          <h2>活動日誌</h2>
          <ul
            v-if="data.activityLog.length > 0"
            class="activity-log"
          >
            <li
              v-for="(event, index) in data.activityLog"
              :key="`${event.occurredAtUtc}-${index}`"
            >
              <time>{{ formatDateTime(event.occurredAtUtc) }}</time>
              <p>{{ event.description }}</p>
            </li>
          </ul>
          <p
            v-else
            class="empty-hint"
          >
            尚無活動紀錄
          </p>
        </section>
      </div>
    </template>
  </section>
</template>
