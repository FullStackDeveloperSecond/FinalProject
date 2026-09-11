<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { isApiError } from '@doselect/web-shared/api'
import { PagePager } from '@doselect/web-shared/components'
import { formatTaipeiDateTime } from '@doselect/web-shared/datetime'
import { listMembers, getMember, changeMemberStatus, type AdminMember } from '../features/members/api'
import { useAdminAuthStore } from '../features/auth/stores/useAdminAuthStore'

const auth = useAdminAuthStore()
const search = ref('')
const appliedSearch = ref('')
const status = ref('')
const page = ref(1)
const expandedPublicId = ref<string | null>(null)
const selected = ref<AdminMember | null>(null)
const detailLoading = ref(false)
const busy = ref(false)
const message = ref('')
const reason = ref('')
const statusOptions = [
  { value: 'Active', label: '啟用' },
  { value: 'Suspended', label: '停用' },
  { value: 'PendingEmailVerification', label: '待驗證' },
] as const
const labels: Record<string, string> = {
  Active: '啟用',
  PendingEmailVerification: '待驗證',
  Suspended: '停用',
  Anonymized: '停用',
  Disabled: '停用',
}
const canManage = computed(() => auth.currentUser?.roles?.includes('SuperAdmin') ?? false)
watch(search, (value, _old, cleanup) => {
  const timer = setTimeout(() => { appliedSearch.value = value.trim(); page.value = 1 }, 300)
  cleanup(() => clearTimeout(timer))
})
watch(status, () => { page.value = 1 })
const filters = computed(() => ({ Search: appliedSearch.value || undefined, Status: status.value || undefined, Page: page.value, PageSize: 20 }))
const query = useQuery({ queryKey: computed(() => ['admin-members', filters.value]), queryFn: () => listMembers(filters.value) })
let detailGeneration = 0
watch(filters, () => {
  detailGeneration++
  expandedPublicId.value = null
  selected.value = null
  detailLoading.value = false
  reason.value = ''
  message.value = ''
})
function memberDetailId(publicId: string) {
  return `member-detail-${publicId.replace(/[^a-zA-Z0-9_-]/g, '-')}`
}
function closeDetail() {
  detailGeneration++
  expandedPublicId.value = null
  selected.value = null
  detailLoading.value = false
  reason.value = ''
}
async function toggleDetail(publicId: string) {
  if (expandedPublicId.value === publicId) {
    closeDetail()
    return
  }
  const generation = ++detailGeneration
  expandedPublicId.value = publicId
  selected.value = null
  detailLoading.value = true
  message.value = ''
  reason.value = ''
  try {
    const member = await getMember(publicId)
    if (generation === detailGeneration) selected.value = member
  } catch {
    if (generation === detailGeneration) {
      expandedPublicId.value = null
      message.value = '無法載入會員詳情，請重試。'
    }
  } finally {
    if (generation === detailGeneration) detailLoading.value = false
  }
}
async function submit() {
  if (!selected.value || !canManage.value || !reason.value || busy.value) return
  const member = selected.value
  busy.value = true
  message.value = ''
  try {
    await changeMemberStatus(member.publicId, { active: member.status === 'Suspended', rowVersion: member.rowVersion, reasonCode: reason.value })
    expandedPublicId.value = null
    selected.value = null
    detailGeneration++
    await query.refetch()
    message.value = '會員狀態已更新，並已留下稽核紀錄。'
  } catch (error) {
    message.value = isApiError(error) && error.status === 409 ? '會員資料或狀態已變更，請重新開啟詳情確認後再操作。' : '更新失敗，請確認權限與登入狀態後重試。'
  } finally { busy.value = false }
}
</script>

<template>
  <section>
    <h1>會員管理</h1>
    <p>查詢會員與管理帳號狀態；聯絡資訊預設遮蔽，不提供刪除或密碼查閱。</p>
    <div class="members-filters">
      <label>姓名或電子郵件<input
        v-model="search"
        type="search"
        maxlength="100"
        :disabled="busy"
      ></label>
      <label>帳號狀態<select
        v-model="status"
        :disabled="busy"
      ><option value="">全部</option><option
        v-for="option in statusOptions"
        :key="option.value"
        :value="option.value"
      >{{ option.label }}</option></select></label>
    </div>
    <p
      v-if="message"
      role="status"
    >
      {{ message }}
    </p>
    <p v-if="query.isPending.value">
      會員載入中…
    </p>
    <p
      v-else-if="query.isError.value"
      role="alert"
    >
      無法載入會員，請確認登入與查詢權限。<button
        type="button"
        @click="query.refetch()"
      >
        重試
      </button>
    </p>
    <template v-else-if="query.data.value">
      <p>共 {{ query.data.value.totalCount }} 位會員</p>
      <div class="table-scroll">
        <table>
          <thead><tr><th>姓名</th><th>電子郵件</th><th>狀態</th><th>信箱驗證</th><th>操作</th></tr></thead><tbody>
            <template
              v-for="member in query.data.value.items"
              :key="member.publicId"
            >
              <tr class="member-row">
                <td>{{ member.displayName }}</td><td>{{ member.emailMasked }}</td><td>{{ labels[member.status] ?? '未知狀態' }}</td><td>{{ member.emailVerified ? '已驗證' : '未驗證' }}</td><td>
                  <button
                    type="button"
                    :disabled="busy"
                    :aria-expanded="expandedPublicId === member.publicId"
                    :aria-controls="memberDetailId(member.publicId)"
                    @click="toggleDetail(member.publicId)"
                  >
                    {{ expandedPublicId === member.publicId ? '收合' : '詳情' }}
                  </button>
                </td>
              </tr>
              <Transition name="member-detail">
                <tr
                  v-if="expandedPublicId === member.publicId"
                  :key="`${member.publicId}-detail`"
                  class="member-detail-row"
                >
                  <td colspan="5">
                    <div class="member-detail__reveal">
                      <section
                        :id="memberDetailId(member.publicId)"
                        class="card member-detail__panel"
                        :aria-labelledby="`${memberDetailId(member.publicId)}-title`"
                      >
                        <h2 :id="`${memberDetailId(member.publicId)}-title`">
                          {{ member.displayName }}：會員詳情
                        </h2>
                        <p v-if="detailLoading || !selected">
                          會員詳情載入中…
                        </p>
                        <template v-else>
                          <dl class="member-detail__facts">
                            <div><dt>電子郵件</dt><dd>{{ selected.emailMasked }}</dd></div>
                            <div><dt>帳號狀態</dt><dd>{{ labels[selected.status] ?? '停用' }}</dd></div>
                            <div><dt>建立時間</dt><dd>{{ formatTaipeiDateTime(selected.createdAtUtc) }}</dd></div>
                            <div><dt>更新時間</dt><dd>{{ formatTaipeiDateTime(selected.updatedAtUtc) }}</dd></div>
                          </dl>
                          <form
                            v-if="canManage && ['Active', 'Suspended'].includes(selected.status)"
                            class="member-detail__action"
                            @submit.prevent="submit"
                          >
                            <p>{{ selected.status === 'Active' ? '確認停用此會員？停用後現有登入將失效。' : '確認重新啟用此會員？未驗證的信箱仍不可啟用。' }}</p>
                            <div class="member-detail__action-row">
                              <label>操作原因 *<select
                                v-model="reason"
                                required
                                :disabled="busy"
                              ><option value="">請選擇</option><option value="user_request">會員要求</option><option value="policy_violation">違反使用規範</option><option value="resolved">問題已處理</option></select></label>
                              <button
                                type="submit"
                                :disabled="busy || !reason"
                              >
                                {{ busy ? '處理中…' : selected.status === 'Active' ? '確認停用' : '確認啟用' }}
                              </button>
                            </div>
                          </form>
                          <button
                            type="button"
                            class="member-detail__close"
                            :disabled="busy"
                            @click="closeDetail"
                          >
                            關閉詳情
                          </button>
                        </template>
                      </section>
                    </div>
                  </td>
                </tr>
              </Transition>
            </template>
          </tbody>
        </table>
      </div>
      <PagePager
        v-model:page="page"
        :total-records="query.data.value.totalCount"
        :page-size="20"
        aria-label="會員分頁"
      />
    </template>
  </section>
</template>

<style scoped>
.members-filters { display: flex; flex-wrap: wrap; gap: 1rem; margin-block: 1rem; }
.members-filters label { display: grid; gap: .4rem; }
.member-detail-row > td { padding: 0; background: var(--color-surface-soft); }
.member-detail__reveal { display: grid; grid-template-rows: 1fr; overflow: hidden; }
.member-detail__panel { min-height: 0; margin: .75rem; overflow: hidden; }
.member-detail-enter-active .member-detail__reveal,
.member-detail-leave-active .member-detail__reveal { transition: grid-template-rows .24s ease, opacity .18s ease, transform .24s ease; }
.member-detail-enter-from .member-detail__reveal,
.member-detail-leave-to .member-detail__reveal { grid-template-rows: 0fr; opacity: 0; transform: translateY(-.5rem); }
.member-detail__facts { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 1rem 1.5rem; margin: 1.5rem 0; }
.member-detail__facts div { display: grid; gap: .35rem; padding: .9rem 1rem; border: 1px solid var(--color-border-soft); border-radius: .65rem; background: var(--color-surface-strong); }
.member-detail__facts dt { color: var(--color-text-muted); font-size: .875rem; }
.member-detail__facts dd { margin: 0; font-weight: 600; overflow-wrap: anywhere; }
.member-detail__action { display: grid; gap: 1rem; padding: 1rem; border: 1px solid var(--color-border-soft); border-radius: .75rem; }
.member-detail__action > p { margin: 0; }
.member-detail__action-row { display: flex; flex-wrap: wrap; align-items: end; gap: 1rem; }
.member-detail__action-row label { display: grid; gap: .4rem; }
.member-detail__close { margin-top: 1rem; }
@media (max-width: 42rem) { .member-detail__facts { grid-template-columns: 1fr; } }
@media (prefers-reduced-motion: reduce) {
  .member-detail-enter-active .member-detail__reveal,
  .member-detail-leave-active .member-detail__reveal { transition: none; }
}
</style>
