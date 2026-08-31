//Shared file-download helpers.
//
//The Blob -> object URL -> hidden anchor -> click -> revoke dance was written out three
//times in appstate.ts. It is collected here because the offline renderer needs a fourth
//caller, and because the naming convention is worth having in one place.

/** `AgroEco-YYYY-MM-DD_H-M-S<suffix>.<ext>`, matching every existing export. */
export function timestampedName(suffix: string, ext: string): string {
    const now = new Date();
    const date = now.toISOString().split('T')[0];
    return `AgroEco-${date}_${now.getHours()}-${now.getMinutes()}-${now.getSeconds()}${suffix}.${ext}`;
}

export function downloadBlob(blob: Blob, suffix: string, ext: string) {
    const url = window.URL.createObjectURL(blob);

    const a = document.createElement('a');
    a.style.setProperty('display', 'none');
    document.body.appendChild(a);
    a.href = url;
    a.download = timestampedName(suffix, ext);
    a.click();
    window.URL.revokeObjectURL(url);
    a.remove();
}

export function downloadText(data: string, ext: string, suffix = '') {
    downloadBlob(new Blob([data], { type: 'text/plain' }), suffix, ext);
}

export function downloadBinary(data: BlobPart, ext: string, suffix = '') {
    downloadBlob(new Blob([data], { type: 'application/octet-stream' }), suffix, ext);
}
