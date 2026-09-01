<script setup lang="ts">
import { computed, ref } from 'vue'
import { EmptyState, ErrorState, LoadingState } from '@doselect/web-shared/components'
import { isApiError } from '@doselect/web-shared/api'
import AddressForm from '../../features/members/components/AddressForm.vue'
import type { CreateMemberAddressRequest, MemberAddress } from '../../features/members/api'
import {
  useAddressesQuery,
  useCreateAddressMutation,
  useDeleteAddressMutation,
  useUpdateAddressMutation,
} from '../../features/members/queries'

const addressesQuery = useAddressesQuery()
const createMutation = useCreateAddressMutation()
const updateMutation = useUpdateAddressMutation()
const deleteMutation = useDeleteAddressMutation()

const showCreateForm = ref(false)
const editingAddressPublicId = ref<string | null>(null)
const formError = ref<string | null>(null)
const deletingAddressPublicId = ref<string | null>(null)
const confirmingDeleteAddressPublicId = ref<string | null>(null)
const deleteError = ref<{ addressPublicId: string, message: string } | null>(null)

const isBusy = computed(() =>
  createMutation.isPending.value
  || updateMutation.isPending.value
  || deleteMutation.isPending.value,
)

function describeError(error: unknown): string {
  if (isApiError(error) && error.code === 'concurrency_conflict') {
    return '這筆地址已被更新，請重新整理後再試一次。'
  }
  return isApiError(error) ? error.message : '操作失敗，請稍後再試。'
}

function startCreate(): void {
  if (isBusy.value) {
    return
  }
  editingAddressPublicId.value = null
  confirmingDeleteAddressPublicId.value = null
  formError.value = null
  showCreateForm.value = true
}

function startEdit(addressPublicId: string): void {
  if (isBusy.value) {
    return
  }
  showCreateForm.value = false
  confirmingDeleteAddressPublicId.value = null
  formError.value = null
  editingAddressPublicId.value = addressPublicId
}

function cancelForm(): void {
  showCreateForm.value = false
  editingAddressPublicId.value = null
  formError.value = null
}

function requestDelete(addressPublicId: string): void {
  if (isBusy.value) {
    return
  }
  showCreateForm.value = false
  editingAddressPublicId.value = null
  deleteError.value = null
  confirmingDeleteAddressPublicId.value = addressPublicId
}

function cancelDelete(): void {
  confirmingDeleteAddressPublicId.value = null
}

async function submitCreate(payload: CreateMemberAddressRequest): Promise<void> {
  formError.value = null
  try {
    await createMutation.mutateAsync(payload)
    showCreateForm.value = false
  } catch (error) {
    formError.value = describeError(error)
  }
}

async function submitEdit(address: MemberAddress, payload: CreateMemberAddressRequest): Promise<void> {
  formError.value = null
  try {
    await updateMutation.mutateAsync({
      addressPublicId: address.publicId,
      body: { ...payload, rowVersion: address.rowVersion },
    })
    editingAddressPublicId.value = null
  } catch (error) {
    formError.value = describeError(error)
  }
}

async function remove(address: MemberAddress): Promise<void> {
  if (isBusy.value) {
    return
  }
  deleteError.value = null
  confirmingDeleteAddressPublicId.value = null
  deletingAddressPublicId.value = address.publicId
  try {
    await deleteMutation.mutateAsync({ addressPublicId: address.publicId, rowVersion: address.rowVersion })
  } catch (error) {
    deleteError.value = { addressPublicId: address.publicId, message: describeError(error) }
  } finally {
    deletingAddressPublicId.value = null
  }
}
</script>

<template>
  <section
    class="addresses-page"
    aria-labelledby="addresses-title"
  >
    <header class="addresses-page__header">
      <h1 id="addresses-title">
        收件地址
      </h1>
      <button
        v-if="!showCreateForm"
        type="button"
        :disabled="isBusy"
        @click="startCreate"
      >
        新增地址
      </button>
    </header>

    <LoadingState
      v-if="addressesQuery.isPending.value"
      label="收件地址載入中"
    />
    <ErrorState
      v-else-if="addressesQuery.isError.value"
      :description="describeError(addressesQuery.error.value)"
      @retry="addressesQuery.refetch"
    />
    <template v-else>
      <AddressForm
        v-if="showCreateForm"
        :submitting="createMutation.isPending.value"
        @submit="submitCreate"
        @cancel="cancelForm"
      />
      <p
        v-if="showCreateForm && formError"
        class="addresses-page__error"
        role="alert"
      >
        {{ formError }}
      </p>

      <EmptyState
        v-if="!showCreateForm && addressesQuery.data.value?.length === 0"
        title="尚未新增收件地址"
        description="新增地址後，結帳時可以直接選用。"
      />

      <ul
        v-if="(addressesQuery.data.value?.length ?? 0) > 0"
        class="addresses-page__list"
      >
        <li
          v-for="address in addressesQuery.data.value"
          :key="address.publicId"
          class="address-card"
        >
          <template v-if="editingAddressPublicId === address.publicId">
            <AddressForm
              :address="address"
              :submitting="updateMutation.isPending.value"
              @submit="(payload) => submitEdit(address, payload)"
              @cancel="cancelForm"
            />
            <p
              v-if="formError"
              class="addresses-page__error"
              role="alert"
            >
              {{ formError }}
            </p>
          </template>
          <template v-else>
            <div class="address-card__info">
              <p class="address-card__label">
                {{ address.label }}
                <span
                  v-if="address.isDefault"
                  class="address-card__default-badge"
                >預設</span>
              </p>
              <p>{{ address.recipientName }} ／ {{ address.phone }}</p>
              <p>{{ address.postalCode }} {{ address.city }}{{ address.district }}{{ address.addressLine1 }}{{ address.addressLine2 }}</p>
            </div>
            <div
              v-if="confirmingDeleteAddressPublicId === address.publicId"
              class="address-card__delete-confirm"
              role="alertdialog"
              :aria-label="`確認刪除地址「${address.label}」`"
            >
              <p>確定要刪除這筆收件地址嗎？歷史訂單的地址快照不會受到影響。</p>
              <div class="address-card__actions">
                <button
                  type="button"
                  :disabled="isBusy"
                  @click="remove(address)"
                >
                  確定刪除
                </button>
                <button
                  type="button"
                  :disabled="isBusy"
                  @click="cancelDelete"
                >
                  取消
                </button>
              </div>
            </div>
            <div
              v-else
              class="address-card__actions"
            >
              <button
                type="button"
                :disabled="isBusy"
                @click="startEdit(address.publicId)"
              >
                編輯
              </button>
              <button
                type="button"
                :disabled="isBusy || deletingAddressPublicId === address.publicId"
                @click="requestDelete(address.publicId)"
              >
                {{ deletingAddressPublicId === address.publicId ? '刪除中…' : '刪除' }}
              </button>
            </div>
            <p
              v-if="deleteError?.addressPublicId === address.publicId"
              class="addresses-page__error"
              role="alert"
            >
              {{ deleteError.message }}
            </p>
          </template>
        </li>
      </ul>
    </template>
  </section>
</template>

<style scoped>
.addresses-page {
  display: grid;
  gap: 1.5rem;
  max-width: 40rem;
}

.addresses-page__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.addresses-page__list {
  display: grid;
  gap: 0.75rem;
  padding: 0;
  list-style: none;
}

.address-card {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.75rem;
}

.address-card__info {
  display: grid;
  gap: 0.25rem;
}

.address-card__label {
  margin: 0;
  font-weight: 600;
}

.address-card__default-badge {
  margin-left: 0.5rem;
  padding: 0.125rem 0.5rem;
  border-radius: 999px;
  background: #dcfce7;
  color: #166534;
  font-size: 0.75rem;
}

.address-card__actions {
  display: flex;
  gap: 0.5rem;
  flex-shrink: 0;
}

.address-card__delete-confirm {
  display: grid;
  gap: 0.5rem;
  max-width: 18rem;
}

.address-card__delete-confirm p {
  margin: 0;
}

.addresses-page__error {
  margin: 0;
  color: #b91c1c;
}
</style>
