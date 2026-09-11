<script setup lang="ts">
import { computed, nextTick, reactive, ref, watch } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { isApiError } from '@doselect/web-shared/api'
import { PagePager } from '@doselect/web-shared/components'
import { useAdminAuthStore } from '../../features/auth/stores/useAdminAuthStore'
import {
  createAdminAccount,
  listAdminAccounts,
  resendAdminInvitation,
  updateAdminRoles,
  type AdminAccount,
  type AdminAccountFilters,
} from '../../features/adminAccounts/api'

const roleOptions = [
  ['SuperAdmin', '最高管理員'],
  ['CatalogManager', '商品管理'],
  ['InventoryManager', '庫存管理'],
  ['OrderManager', '訂單與物流'],
  ['FinanceManager', '財務管理'],
  ['CustomerService', '客服人員'],
  ['CustomerServiceSupervisor', '客服主管'],
  ['MarketingAnalyst', '行銷分析'],
  ['PrivacyAdmin', '隱私管理'],
  ['SecurityAdmin', '安全管理'],
] as const
const statusLabels: Record<string, string> = {
  Active: '啟用',
  PendingEmailVerification: '待接受邀請',
  Suspended: '停用',
  Disabled: '停用',
  Anonymized: '停用',
}
const reasonOptions = [
  ['job_change', '職務調整'],
  ['staffing_change', '人員異動'],
  ['permission_correction', '權限更正'],
] as const

const auth = useAdminAuthStore()
const search = ref('')
const appliedSearch = ref('')
const status = ref<NonNullable<AdminAccountFilters['Status']> | ''>('')
const role = ref('')
const page = ref(1)
const busy = ref(false)
const message = ref('')
const errorMessage = ref('')
const createDialog = ref<HTMLDialogElement | null>(null)
const editDialog = ref<HTMLDialogElement | null>(null)
const editing = ref<AdminAccount | null>(null)
const editRoles = ref<string[]>([])
const editReason = ref('')
const confirmSuperAdmin = ref(false)

const createForm = reactive({
  email: '',
  password: '',
  passwordConfirmation: '',
  displayName: '',
  employeeCode: '',
  roles: [] as string[],
  confirmSuperAdmin: false,
})
const createPasswordMismatch = computed(() => (
  createForm.passwordConfirmation.length > 0
  && createForm.password !== createForm.passwordConfirmation
))
const createPasswordReady = computed(() => (
  createForm.password.length >= 12
  && createForm.password === createForm.passwordConfirmation
))

watch(search, (value, _old, cleanup) => {
  const timer = setTimeout(() => { appliedSearch.value = value.trim(); page.value = 1 }, 300)
  cleanup(() => clearTimeout(timer))
})
watch([status, role], () => { page.value = 1 })
const filters = computed(() => ({
  Search: appliedSearch.value || undefined,
  Status: status.value || undefined,
  Role: role.value || undefined,
  Page: page.value,
  PageSize: 20,
}))
const query = useQuery({
  queryKey: computed(() => ['admin-accounts', filters.value]),
  queryFn: () => listAdminAccounts(filters.value),
})

function setRole(target: string[], selectedRole: string, event: Event) {
  const checked = (event.target as HTMLInputElement).checked
  if (checked && !target.includes(selectedRole)) target.push(selectedRole)
  if (!checked) {
    const index = target.indexOf(selectedRole)
    if (index >= 0) target.splice(index, 1)
  }
}

function openCreate() {
  Object.assign(createForm, {
    email: '',
    password: '',
    passwordConfirmation: '',
    displayName: '',
    employeeCode: '',
    roles: [],
    confirmSuperAdmin: false,
  })
  message.value = ''
  errorMessage.value = ''
  void nextTick(() => createDialog.value?.showModal())
}

function openEdit(account: AdminAccount) {
  editing.value = account
  editRoles.value = [...account.roles]
  editReason.value = ''
  confirmSuperAdmin.value = false
  message.value = ''
  errorMessage.value = ''
  void nextTick(() => editDialog.value?.showModal())
}

function describeError(error: unknown): string {
  if (!isApiError(error)) return '操作失敗，請稍後重試。'
  const messages: Record<string, string> = {
    admin_email_duplicate: '這個電子郵件已被使用。',
    admin_employee_code_duplicate: '這個員工編號已被使用。',
    admin_role_invalid: '請至少選擇一個有效角色。',
    super_admin_confirmation_required: '授予最高管理員前必須完成二次確認。',
    self_demotion_forbidden: '不可移除自己的最高管理員權限。',
    last_super_admin_required: '系統至少必須保留一位有效的最高管理員。',
    concurrency_conflict: '資料已被其他管理員更新，請重新載入後再操作。',
  }
  return messages[error.code] ?? error.message ?? '操作失敗，請稍後重試。'
}

async function submitCreate() {
  if (busy.value || createForm.roles.length === 0 || !createPasswordReady.value) return
  busy.value = true
  errorMessage.value = ''
  try {
    await createAdminAccount({
      email: createForm.email.trim(),
      password: createForm.password,
      displayName: createForm.displayName.trim(),
      employeeCode: createForm.employeeCode.trim(),
      roles: [...createForm.roles],
      confirmSuperAdmin: createForm.confirmSuperAdmin,
    })
    createDialog.value?.close()
    await query.refetch()
    message.value = '管理員帳號已建立；首次登入時必須設定 TOTP。'
  } catch (error) {
    errorMessage.value = describeError(error)
  } finally {
    busy.value = false
  }
}

async function submitRoles() {
  if (!editing.value || busy.value || editRoles.value.length === 0 || !editReason.value) return
  busy.value = true
  errorMessage.value = ''
  try {
    await updateAdminRoles(editing.value.publicId, {
      roles: [...editRoles.value],
      rowVersion: editing.value.rowVersion,
      reasonCode: editReason.value,
      confirmSuperAdmin: confirmSuperAdmin.value,
    })
    editDialog.value?.close()
    editing.value = null
    await query.refetch()
    message.value = '管理員權限已更新；該帳號必須重新登入。'
  } catch (error) {
    errorMessage.value = describeError(error)
  } finally {
    busy.value = false
  }
}

async function resend(account: AdminAccount) {
  if (busy.value) return
  busy.value = true
  message.value = ''
  errorMessage.value = ''
  try {
    await resendAdminInvitation(account.publicId, account.rowVersion)
    await query.refetch()
    message.value = '舊邀請已失效，新邀請信已交由郵件服務處理。'
  } catch (error) {
    errorMessage.value = describeError(error)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="admin-accounts">
    <header class="admin-accounts__header">
      <div>
        <h1>管理員帳號</h1>
        <p>新增管理員並管理既有角色；所有異動都會留下稽核紀錄。</p>
      </div>
      <button
        type="button"
        @click="openCreate"
      >
        新增管理員
      </button>
    </header>

    <form
      class="admin-accounts__filters"
      aria-label="管理員搜尋"
      @submit.prevent
    >
      <label>關鍵字<input
        v-model="search"
        type="search"
        maxlength="100"
        placeholder="姓名、員工編號或 Email"
      ></label>
      <label>帳號狀態<select v-model="status"><option value="">全部</option><option value="active">啟用</option><option value="pendingEmailVerification">待接受邀請</option><option value="suspended">停用</option></select></label>
      <label>角色<select v-model="role"><option value="">全部</option><option
        v-for="option in roleOptions"
        :key="option[0]"
        :value="option[0]"
      >{{ option[1] }}</option></select></label>
    </form>

    <p
      v-if="message"
      class="admin-accounts__success"
      role="status"
    >
      {{ message }}
    </p>
    <p
      v-if="errorMessage"
      class="admin-accounts__error"
      role="alert"
    >
      {{ errorMessage }}
    </p>
    <p v-if="query.isPending.value">
      管理員資料載入中…
    </p>
    <div
      v-else-if="query.isError.value"
      role="alert"
    >
      無法載入管理員資料。 <button
        type="button"
        @click="query.refetch()"
      >
        重試
      </button>
    </div>
    <template v-else-if="query.data.value">
      <p>共 {{ query.data.value.totalCount }} 位管理員</p>
      <p v-if="query.data.value.items.length === 0">
        沒有符合條件的管理員。
      </p>
      <div
        v-else
        class="table-scroll"
        role="region"
        aria-label="管理員帳號列表"
        tabindex="0"
      >
        <table>
          <thead><tr><th>管理員</th><th>電子郵件</th><th>狀態</th><th>角色</th><th>MFA</th><th>操作</th></tr></thead>
          <tbody>
            <tr
              v-for="account in query.data.value.items"
              :key="account.publicId"
            >
              <td><strong>{{ account.displayName }}</strong><small>{{ account.employeeCode }}</small></td>
              <td>{{ account.email }}</td>
              <td>{{ statusLabels[account.status] ?? account.status }}</td>
              <td>
                <span
                  v-for="assignedRole in account.roles"
                  :key="assignedRole"
                  class="admin-accounts__role"
                >{{ roleOptions.find(option => option[0] === assignedRole)?.[1] ?? assignedRole }}</span>
              </td>
              <td>{{ account.twoFactorEnabled ? '已綁定' : '未綁定' }}</td>
              <td class="admin-accounts__actions">
                <button
                  type="button"
                  :disabled="busy"
                  @click="openEdit(account)"
                >
                  編輯權限
                </button>
                <button
                  v-if="account.status === 'PendingEmailVerification'"
                  type="button"
                  :disabled="busy"
                  @click="resend(account)"
                >
                  重寄邀請
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <PagePager
        v-model:page="page"
        :total-records="query.data.value.totalCount"
        :page-size="20"
        aria-label="管理員分頁"
      />
    </template>

    <dialog
      ref="createDialog"
      class="admin-accounts__dialog"
      @close="errorMessage = ''"
    >
      <form
        class="admin-accounts__form"
        @submit.prevent="submitCreate"
      >
        <h2>新增管理員</h2>
        <label for="create-display-name">顯示名稱 *<input
          id="create-display-name"
          v-model="createForm.displayName"
          class="admin-accounts__control"
          type="text"
          maxlength="100"
          required
        ></label>
        <label for="create-employee-code">員工編號 *<input
          id="create-employee-code"
          v-model="createForm.employeeCode"
          class="admin-accounts__control"
          type="text"
          maxlength="64"
          pattern="[A-Za-z0-9][A-Za-z0-9_-]{1,63}"
          required
        ></label>
        <label for="create-email">登入帳號（電子郵件） *<input
          id="create-email"
          v-model="createForm.email"
          class="admin-accounts__control"
          type="email"
          maxlength="320"
          autocomplete="username"
          required
        ></label>
        <label for="create-password">初始密碼 *<input
          id="create-password"
          v-model="createForm.password"
          class="admin-accounts__control"
          type="password"
          minlength="12"
          maxlength="128"
          autocomplete="new-password"
          required
        ></label>
        <small class="admin-accounts__hint">至少 12 個字元；新管理員首次登入後仍須完成 TOTP 綁定。</small>
        <label for="create-password-confirmation">確認初始密碼 *<input
          id="create-password-confirmation"
          v-model="createForm.passwordConfirmation"
          class="admin-accounts__control"
          type="password"
          minlength="12"
          maxlength="128"
          autocomplete="new-password"
          required
          :aria-invalid="createPasswordMismatch"
          :aria-describedby="createPasswordMismatch ? 'create-password-confirmation-error' : undefined"
        ></label>
        <p
          v-if="createPasswordMismatch"
          id="create-password-confirmation-error"
          class="admin-accounts__error"
          role="alert"
        >
          兩次輸入的密碼不一致。
        </p>
        <fieldset>
          <legend>角色 *</legend><label
            v-for="option in roleOptions"
            :key="option[0]"
            class="admin-accounts__checkbox"
          ><input
            type="checkbox"
            :checked="createForm.roles.includes(option[0])"
            @change="setRole(createForm.roles, option[0], $event)"
          > {{ option[1] }}</label>
        </fieldset>
        <label
          v-if="createForm.roles.includes('SuperAdmin')"
          class="admin-accounts__checkbox admin-accounts__warning"
        ><input
          v-model="createForm.confirmSuperAdmin"
          type="checkbox"
          required
        > 我確認要授予此帳號最高管理權限</label>
        <p
          v-if="errorMessage"
          role="alert"
          class="admin-accounts__error"
        >
          {{ errorMessage }}
        </p>
        <div class="admin-accounts__form-actions">
          <button
            type="submit"
            :disabled="busy || createForm.roles.length === 0 || !createPasswordReady"
          >
            {{ busy ? '建立中…' : '建立管理員' }}
          </button><button
            type="button"
            :disabled="busy"
            @click="createDialog?.close()"
          >
            取消
          </button>
        </div>
      </form>
    </dialog>

    <dialog
      ref="editDialog"
      class="admin-accounts__dialog"
      @close="errorMessage = ''"
    >
      <form
        v-if="editing"
        class="admin-accounts__form"
        @submit.prevent="submitRoles"
      >
        <h2>編輯 {{ editing.displayName }} 的權限</h2>
        <p>{{ editing.employeeCode }} · {{ editing.email }}</p>
        <fieldset>
          <legend>角色 *</legend><label
            v-for="option in roleOptions"
            :key="option[0]"
            class="admin-accounts__checkbox"
          ><input
            type="checkbox"
            :checked="editRoles.includes(option[0])"
            @change="setRole(editRoles, option[0], $event)"
          > {{ option[1] }}</label>
        </fieldset>
        <label>異動原因 *<select
          v-model="editReason"
          required
        ><option value="">請選擇</option><option
          v-for="option in reasonOptions"
          :key="option[0]"
          :value="option[0]"
        >{{ option[1] }}</option></select></label>
        <label
          v-if="editRoles.includes('SuperAdmin') && !editing.roles.includes('SuperAdmin')"
          class="admin-accounts__checkbox admin-accounts__warning"
        ><input
          v-model="confirmSuperAdmin"
          type="checkbox"
          required
        > 我確認要授予此帳號最高管理權限</label>
        <p
          v-if="editing.publicId === auth.currentUser?.publicId"
          class="admin-accounts__warning"
        >
          你正在編輯自己的帳號；系統不允許移除自己的最高管理權限。
        </p>
        <p
          v-if="errorMessage"
          role="alert"
          class="admin-accounts__error"
        >
          {{ errorMessage }}
        </p>
        <div class="admin-accounts__form-actions">
          <button
            type="submit"
            :disabled="busy || editRoles.length === 0 || !editReason"
          >
            {{ busy ? '儲存中…' : '儲存權限' }}
          </button><button
            type="button"
            :disabled="busy"
            @click="editDialog?.close()"
          >
            取消
          </button>
        </div>
      </form>
    </dialog>
  </section>
</template>

<style scoped>
.admin-accounts { display: grid; gap: 1.25rem; }
.admin-accounts__header { display: flex; justify-content: space-between; align-items: start; gap: 1rem; }
.admin-accounts__header h1, .admin-accounts__header p { margin-block: 0 .5rem; }
.admin-accounts__filters { display: grid; grid-template-columns: minmax(16rem, 2fr) repeat(2, minmax(10rem, 1fr)); gap: 1rem; padding: 1rem; border: 1px solid var(--color-border-strong); border-radius: var(--radius-md); background: var(--color-surface); }
.admin-accounts__filters label, .admin-accounts__form > label { display: grid; gap: .4rem; font-weight: 700; }
.admin-accounts td small { display: block; margin-top: .25rem; color: var(--color-text-muted); }
.admin-accounts__role { display: inline-block; margin: .15rem .3rem .15rem 0; padding: .2rem .5rem; border-radius: 999px; background: var(--color-section); white-space: nowrap; }
.admin-accounts__actions { display: flex; flex-wrap: wrap; gap: .5rem; }
.admin-accounts__success { color: #047857; }
.admin-accounts__error, .admin-accounts__warning { color: #b91c1c; }
.admin-accounts__dialog { width: min(40rem, calc(100vw - 2rem)); max-height: calc(100vh - 2rem); padding: 0; border: 1px solid var(--color-border-strong); border-radius: var(--radius-md); background: var(--color-surface); color: var(--color-text); box-shadow: var(--shadow-lg); }
.admin-accounts__dialog::backdrop { background: rgb(9 30 45 / 55%); }
.admin-accounts__form { display: grid; gap: 1rem; padding: 1.5rem; }
.admin-accounts__control { width: 100%; min-width: 0; }
.admin-accounts__hint { margin-top: -.65rem; color: var(--color-text-muted); }
.admin-accounts__form fieldset { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .65rem 1rem; padding: 1rem; border: 1px solid var(--color-border); border-radius: var(--radius-sm); }
.admin-accounts__form .admin-accounts__checkbox { display: grid; grid-template-columns: 1.1rem minmax(0, 1fr); align-items: start; gap: .5rem; }
.admin-accounts__checkbox input[type='checkbox'] { width: 1rem; height: 1rem; margin: .25rem 0 0; }
.admin-accounts__form-actions { display: flex; justify-content: flex-end; gap: .75rem; }
@media (max-width: 48rem) { .admin-accounts__header { display: grid; } .admin-accounts__filters, .admin-accounts__form fieldset { grid-template-columns: 1fr; } }
</style>
