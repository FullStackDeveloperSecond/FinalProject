/**
 * Vue's SFC script-block parser does not resolve ambient DOM lib globals (`File`, `Event`,
 * `HTMLInputElement`, ...) the same way plain `.ts` files do, which makes ESLint's `no-undef`
 * rule report false positives when those identifiers are written directly inside a `.vue`
 * `<script setup>` block. Declaring them here (a plain `.ts` module, where they resolve
 * correctly) and re-exporting as local aliases lets consuming SFCs reference the aliases
 * instead of the bare global identifiers.
 */
export type AttachmentFile = File
export type AttachmentChangeEvent = Event
export type AttachmentInputElement = HTMLInputElement

export const acceptedAttachmentExtensions = ['.png', '.jpg', '.jpeg', '.pdf']
const acceptedAttachmentTypes = ['image/png', 'image/jpeg', 'application/pdf']
export const maxAttachmentSizeBytes = 10 * 1024 * 1024

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`
  }
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function isAcceptedAttachment(file: AttachmentFile): boolean {
  const name = file.name.toLowerCase()
  const hasAcceptedExtension = acceptedAttachmentExtensions.some((extension) => name.endsWith(extension))
  const hasAcceptedType = file.type === '' || acceptedAttachmentTypes.includes(file.type)
  return hasAcceptedExtension && hasAcceptedType
}

export function getChangeEventFile(event: AttachmentChangeEvent): AttachmentFile | null {
  const input = event.target as AttachmentInputElement
  return input.files?.[0] ?? null
}
