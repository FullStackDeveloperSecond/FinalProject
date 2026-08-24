<script setup lang="ts">
import { computed, reactive } from 'vue'
import { useRouter } from 'vue-router'
import { LoadingState, ErrorState, EmptyState } from '@doselect/web-shared/components'
import { useAdminMemberListQuery, type AdminMemberListFilters } from '../queries/useAdminMembers'

const router = useRouter()

const filters = reactive<AdminMemberListFilters>({
  search: '',
  status: '',
  registeredFrom: '',
  registeredTo: '',
  pageNumber: 1,
  pageSize: 10,
})

const filtersRef = computed(() => filters)
const { data, isPending, isError, error, refetch } = useAdminMemberListQuery(filtersRef)

const totalPages = computed(() => {
  if (!data.value) {
    return 1
  }
  return Math.max(1, Math.ceil(data.value.members.totalCount / data.value.members.pageSize))
})

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

function statusClass(status: string): string {
  return status === 'Active' ? 'badge badge--active' : 'badge badge--inactive'
}

function formatDate(value: string): string {
  return new Date(value).toLocaleString('zh-TW', { hour12: false })
}

function applySearch(): void {
  filters.pageNumber = 1
  void refetch()
}

function goToPage(page: number): void {
  if (page < 1 || page > totalPages.value) {
    return
  }
  filters.pageNumber = page
}

function viewMember(publicId: string): void {
  void router.push(`/members/${publicId}`)
}
</script>

<template>
  <section class="page">
    <header class="page__header">
      <h1>全體會員列表</h1>
      <div class="stat-chips">
        <div class="stat-chip">
          <span class="stat-chip__label">總會員數</span>
          <strong>{{ data?.stats.totalMembers ?? '—' }}</strong>
        </div>
        <div class="stat-chip">
          <span class="stat-chip__label">今日新註冊</span>
          <strong>{{ data?.stats.newTodayCount ?? '—' }}</strong>
        </div>
        <div class="stat-chip">
          <span class="stat-chip__label">活躍會員</span>
          <strong>{{ data?.stats.activeCount ?? '—' }}</strong>
        </div>
      </div>
    </header>

    <form
      class="filter-bar"
      @submit.prevent="applySearch"
    >
      <input
        v-model="filters.search"
        type="search"
        placeholder="搜尋姓名或電子郵件"
        class="filter-bar__search"
      >
      <label class="filter-bar__field">
        起始日期
        <input
          v-model="filters.registeredFrom"
          type="date"
        >
      </label>
      <label class="filter-bar__field">
        結束日期
        <input
          v-model="filters.registeredTo"
          type="date"
        >
      </label>
      <select
        v-model="filters.status"
        class="filter-bar__field"
      >
        <option value="">
          全部狀態
        </option>
        <option value="Active">
          啟用
        </option>
        <option value="Suspended">
          停用
        </option>
        <option value="PendingEmailVerification">
          待驗證
        </option>
      </select>
      <button type="submit">
        搜尋
      </button>
    </form>

    <LoadingState v-if="isPending" />
    <ErrorState
      v-else-if="isError"
      :description="error?.message"
      @retry="refetch"
    />
    <EmptyState v-else-if="data && data.members.items.length === 0" />

    <template v-else-if="data">
      <table class="data-table">
        <thead>
          <tr>
            <th>ID</th>
            <th>姓名</th>
            <th>電子郵件</th>
            <th>註冊日期</th>
            <th>狀態</th>
            <th />
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="member in data.members.items"
            :key="member.publicId"
          >
            <td class="data-table__id">
              {{ member.publicId.slice(0, 8) }}
            </td>
            <td>{{ member.displayName }}</td>
            <td>{{ member.email }}</td>
            <td>{{ formatDate(member.registeredAtUtc) }}</td>
            <td>
              <span :class="statusClass(member.accountStatus)">{{ statusLabel(member.accountStatus) }}</span>
            </td>
            <td class="data-table__actions">
              <button
                type="button"
                @click="viewMember(member.publicId)"
              >
                查看
              </button>
            </td>
          </tr>
        </tbody>
      </table>

      <nav
        class="pagination"
        aria-label="分頁"
      >
        <button
          type="button"
          :disabled="filters.pageNumber <= 1"
          @click="goToPage(filters.pageNumber - 1)"
        >
          上一頁
        </button>
        <span>第 {{ filters.pageNumber }} / {{ totalPages }} 頁</span>
        <button
          type="button"
          :disabled="filters.pageNumber >= totalPages"
          @click="goToPage(filters.pageNumber + 1)"
        >
          下一頁
        </button>
      </nav>
    </template>
  </section>
</template>
