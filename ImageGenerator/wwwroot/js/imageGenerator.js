// Map of objectUrl -> { objectUrl, contentType, blob } kept in the browser.
const imageStore = new Map();

export function initDropZone(dropZoneElement, dotNetHelper) {
    let dragCounter = 0;

    function preventDefaults(e) {
        e.preventDefault();
        e.stopPropagation();
    }

    dropZoneElement.addEventListener('dragenter', e => {
        preventDefaults(e);
        dragCounter++;
        if (dragCounter === 1) {
            dotNetHelper.invokeMethodAsync('OnDragEnterAsync');
        }
    });

    dropZoneElement.addEventListener('dragleave', e => {
        preventDefaults(e);
        dragCounter--;
        if (dragCounter <= 0) {
            dragCounter = 0;
            dotNetHelper.invokeMethodAsync('OnDragLeaveAsync');
        }
    });

    dropZoneElement.addEventListener('dragover', preventDefaults);

    dropZoneElement.addEventListener('drop', async e => {
        preventDefaults(e);
        dragCounter = 0;
        dotNetHelper.invokeMethodAsync('OnDragLeaveAsync');

        const files = e.dataTransfer.files;
        if (!files || files.length === 0) return;

        const entries = [];
        for (const file of files) {
            const type = file.type || getTypeFromExtension(file.name);
            if (!isSupportedType(type)) continue;

            const objectUrl = URL.createObjectURL(file);
            imageStore.set(objectUrl, { objectUrl, contentType: type, blob: file });
            entries.push({ name: file.name, contentType: type, objectUrl });
        }

        if (entries.length > 0) {
            await dotNetHelper.invokeMethodAsync('OnFilesDroppedAsync', entries);
        }
    });
}

/** Returns base64-encoded bytes for all given objectUrls, in order. */
export async function getImagesAsBase64(objectUrls) {
    const results = [];
    for (const url of objectUrls) {
        const entry = imageStore.get(url);
        if (!entry) {
            results.push('');
            continue;
        }
        const arrayBuffer = await entry.blob.arrayBuffer();
        const bytes = new Uint8Array(arrayBuffer);
        let binary = '';
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        results.push(btoa(binary));
    }
    return results;
}

/** Revokes Object URLs and clears the store entries for the given urls. */
export function revokeObjectUrls(objectUrls) {
    for (const url of objectUrls) {
        URL.revokeObjectURL(url);
        imageStore.delete(url);
    }
}

function isSupportedType(type) {
    return type === 'image/png' || type === 'image/jpeg' || type === 'image/webp';
}

function getTypeFromExtension(filename) {
    const ext = (filename.split('.').pop() ?? '').toLowerCase();
    const map = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', webp: 'image/webp' };
    return map[ext] ?? '';
}

