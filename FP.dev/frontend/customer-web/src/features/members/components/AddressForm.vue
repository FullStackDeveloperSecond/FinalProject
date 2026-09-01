<script setup lang="ts">
import { reactive, watch } from 'vue'
import type { CreateMemberAddressRequest, MemberAddress } from '../api'

const props = defineProps<{
  address?: MemberAddress | null
  submitting?: boolean
}>()

const emit = defineEmits<{
  submit: [payload: CreateMemberAddressRequest]
  cancel: []
}>()

const form = reactive<CreateMemberAddressRequest>({
  label: props.address?.label ?? '',
  recipientName: props.address?.recipientName ?? '',
  phone: props.address?.phone ?? '',
  postalCode: props.address?.postalCode ?? '',
  city: props.address?.city ?? '',
  district: props.address?.district ?? '',
  addressLine1: props.address?.addressLine1 ?? '',
  addressLine2: props.address?.addressLine2 ?? '',
  isDefault: props.address?.isDefault ?? false,
})

watch(() => props.address, (address) => {
  form.label = address?.label ?? ''
  form.recipientName = address?.recipientName ?? ''
  form.phone = address?.phone ?? ''
  form.postalCode = address?.postalCode ?? ''
  form.city = address?.city ?? ''
  form.district = address?.district ?? ''
  form.addressLine1 = address?.addressLine1 ?? ''
  form.addressLine2 = address?.addressLine2 ?? ''
  form.isDefault = address?.isDefault ?? false
})

function submit(): void {
  emit('submit', {
    label: form.label.trim(),
    recipientName: form.recipientName.trim(),
    phone: form.phone.trim(),
    postalCode: form.postalCode.trim(),
    city: form.city.trim(),
    district: form.district.trim(),
    addressLine1: form.addressLine1.trim(),
    addressLine2: form.addressLine2?.trim() || null,
    isDefault: form.isDefault,
  })
}
</script>

<template>
  <form
    class="address-form"
    @submit.prevent="submit"
  >
    <div class="form-field">
      <label for="address-label">地址名稱</label>
      <input
        id="address-label"
        v-model="form.label"
        type="text"
        placeholder="例如：住家、公司"
        required
        maxlength="50"
      >
    </div>
    <div class="form-field">
      <label for="address-recipient-name">收件人姓名</label>
      <input
        id="address-recipient-name"
        v-model="form.recipientName"
        type="text"
        required
        maxlength="100"
      >
    </div>
    <div class="form-field">
      <label for="address-phone">收件人電話</label>
      <input
        id="address-phone"
        v-model="form.phone"
        type="tel"
        required
        minlength="6"
        maxlength="32"
      >
    </div>
    <div class="form-field">
      <label for="address-postal-code">郵遞區號</label>
      <input
        id="address-postal-code"
        v-model="form.postalCode"
        type="text"
        required
        maxlength="16"
      >
    </div>
    <div class="form-field">
      <label for="address-city">縣市</label>
      <input
        id="address-city"
        v-model="form.city"
        type="text"
        required
        maxlength="50"
      >
    </div>
    <div class="form-field">
      <label for="address-district">鄉鎮市區</label>
      <input
        id="address-district"
        v-model="form.district"
        type="text"
        required
        maxlength="50"
      >
    </div>
    <div class="form-field">
      <label for="address-line1">地址</label>
      <input
        id="address-line1"
        v-model="form.addressLine1"
        type="text"
        required
        maxlength="300"
      >
    </div>
    <div class="form-field">
      <label for="address-line2">地址第二行（選填）</label>
      <input
        id="address-line2"
        v-model="form.addressLine2"
        type="text"
        maxlength="300"
      >
    </div>
    <label class="address-form__default-checkbox">
      <input
        v-model="form.isDefault"
        type="checkbox"
      >
      設為預設地址
    </label>

    <div class="address-form__actions">
      <button
        type="submit"
        :disabled="submitting"
      >
        {{ submitting ? '儲存中…' : '儲存' }}
      </button>
      <button
        type="button"
        :disabled="submitting"
        @click="emit('cancel')"
      >
        取消
      </button>
    </div>
  </form>
</template>

<style scoped>
.address-form {
  display: grid;
  gap: 0.75rem;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 0.75rem;
}

.address-form__default-checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.address-form__actions {
  display: flex;
  gap: 0.5rem;
}
</style>
