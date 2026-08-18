// JavaScript for ALPR Web Application
document.addEventListener('DOMContentLoaded', () => {
    // --- DOM Elements ---
    const htmlElement = document.documentElement;
    const themeToggleBtn = document.getElementById('theme-toggle');
    
    const uploadSection = document.getElementById('upload-section');
    const loadingSection = document.getElementById('loading-section');
    const resultSection = document.getElementById('result-section');
    
    const dropzone = document.getElementById('dropzone');
    const fileInput = document.getElementById('file-input');
    const selectFileBtn = document.getElementById('select-file-btn');
    
    const previewContainer = document.getElementById('preview-container');
    const previewImage = document.getElementById('preview-image');
    const cancelPreviewBtn = document.getElementById('cancel-preview-btn');
    const startDetectBtn = document.getElementById('start-detect-btn');
    const dropzonePrompt = document.getElementById('dropzone-prompt');
    
    const resultImage = document.getElementById('result-image');
    const plateBadge = document.getElementById('plate-badge');
    const plateNumberText = document.getElementById('plate-number-text');
    const vehicleTypeText = document.getElementById('vehicle-type-text');
    const plateColorText = document.getElementById('plate-color-text');
    const confidenceText = document.getElementById('confidence-text');
    const confidenceBar = document.getElementById('confidence-bar');
    const processingTimeText = document.getElementById('processing-time-text');
    
    const copyPlateBtn = document.getElementById('copy-plate-btn');
    const copyToast = document.getElementById('copy-toast');
    const resetBtn = document.getElementById('reset-btn');

    let currentFile = null;

    // --- 1. Theme Toggle (Dark/Light Mode) ---
    function initTheme() {
        const savedTheme = localStorage.getItem('alpr-theme');
        if (savedTheme === 'dark' || (!savedTheme && window.matchMedia('(prefers-color-scheme: dark)').matches)) {
            htmlElement.classList.add('dark');
        } else {
            htmlElement.classList.remove('dark');
        }
    }

    themeToggleBtn?.addEventListener('click', () => {
        htmlElement.classList.toggle('dark');
        const isDark = htmlElement.classList.contains('dark');
        localStorage.setItem('alpr-theme', isDark ? 'dark' : 'light');
    });

    initTheme();

    // --- 2. File Selection & Drag-and-Drop ---
    selectFileBtn?.addEventListener('click', () => fileInput.click());

    fileInput?.addEventListener('change', (e) => {
        if (e.target.files && e.target.files[0]) {
            handleFileSelected(e.target.files[0]);
        }
    });

    ['dragenter', 'dragover'].forEach(eventName => {
        dropzone?.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.add('dropzone-active');
        }, false);
    });

    ['dragleave', 'drop'].forEach(eventName => {
        dropzone?.addEventListener(eventName, (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.remove('dropzone-active');
        }, false);
    });

    dropzone?.addEventListener('drop', (e) => {
        const dt = e.dataTransfer;
        const files = dt.files;
        if (files && files[0]) {
            handleFileSelected(files[0]);
        }
    });

    function handleFileSelected(file) {
        if (!file.type.startsWith('image/')) {
            alert('Vui lòng chọn một tệp hình ảnh hợp lệ (JPG, PNG, WEBP).');
            return;
        }

        currentFile = file;
        const reader = new FileReader();
        reader.onload = (e) => {
            previewImage.src = e.target.result;
            dropzonePrompt.classList.add('hidden');
            previewContainer.classList.remove('hidden');
        };
        reader.readAsDataURL(file);
    }

    cancelPreviewBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        resetFileInput();
    });

    function resetFileInput() {
        currentFile = null;
        fileInput.value = '';
        previewImage.src = '';
        previewContainer.classList.add('hidden');
        dropzonePrompt.classList.remove('hidden');
    }

    /**
     * Nén và resize ảnh ở phía Client bằng HTML5 Canvas trước khi gửi lên Server.
     * Giúp giảm kích thước file từ vài MB xuống vài trăm KB.
     */
    function compressImage(file, maxWidth = 1280, maxHeight = 1280, quality = 0.75) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.readAsDataURL(file);
            reader.onload = (event) => {
                const img = new Image();
                img.src = event.target.result;
                img.onload = () => {
                    let width = img.width;
                    let height = img.height;

                    // Tính toán tỉ lệ resize đảm bảo chiều rộng/chiều cao tối đa <= 1280px
                    if (width > maxWidth || height > maxHeight) {
                        if (width / height > maxWidth / maxHeight) {
                            height = Math.round((height * maxWidth) / width);
                            width = maxWidth;
                        } else {
                            width = Math.round((width * maxHeight) / height);
                            height = maxHeight;
                        }
                    }

                    const canvas = document.createElement('canvas');
                    canvas.width = width;
                    canvas.height = height;

                    const ctx = canvas.getContext('2d');
                    ctx.drawImage(img, 0, 0, width, height);

                    canvas.toBlob(
                        (blob) => {
                            if (blob) {
                                resolve(blob);
                            } else {
                                resolve(file); // Fallback sử dụng file gốc nếu nén lỗi
                            }
                        },
                        'image/jpeg',
                        quality
                    );
                };
                img.onerror = () => resolve(file);
            };
            reader.onerror = () => resolve(file);
        });
    }

    // --- 3. Start Detection API Call ---
    startDetectBtn?.addEventListener('click', async (e) => {
        e.stopPropagation();
        if (!currentFile) return;

        // Show loading state
        uploadSection.classList.add('hidden');
        loadingSection.classList.remove('hidden');
        resultSection.classList.add('hidden');

        try {
            // Nén và resize ảnh client-side trước khi upload
            const fileToSend = await compressImage(currentFile, 1280, 1280, 0.75);

            const formData = new FormData();
            formData.append('file', fileToSend, currentFile.name || 'image.jpg');

            const response = await fetch('/api/detect', {
                method: 'POST',
                body: formData
            });

            if (!response.ok) {
                const errData = await response.json();
                throw new Error(errData.detail || 'Lỗi hệ thống khi nhận diện.');
            }

            const data = await response.json();
            renderResults(data);
        } catch (err) {
            alert(`Lỗi: ${err.message}`);
            // Return to upload state
            loadingSection.classList.add('hidden');
            uploadSection.classList.remove('hidden');
        }
    });

    // --- 4. Render Detection Results ---
    function renderResults(data) {
        // Set preview image in result
        resultImage.src = previewImage.src;

        // Extract values safely with fallbacks
        const plateNum = data.plate_number || "KHÔNG RÕ";
        const vehicleType = data.vehicle_type || "Không rõ";
        const plateColor = data.plate_color || data.plate_type || "Trắng";
        const confidence = (data.confidence !== undefined && data.confidence !== null) ? data.confidence : 99.0;
        const procTime = data.processing_time || "Hoàn tất";

        // Render to DOM
        plateNumberText.textContent = plateNum;
        vehicleTypeText.textContent = vehicleType;
        plateColorText.textContent = plateColor;
        confidenceText.textContent = `${confidence}%`;
        confidenceBar.style.width = `${Math.min(Math.max(confidence, 0), 100)}%`;
        processingTimeText.textContent = procTime;

        // Apply Vietnam plate badge styling safely
        const colorStr = String(plateColor).toLowerCase();
        if (colorStr.includes('vàng') || colorStr.includes('yellow')) {
            plateBadge.className = "vn-plate-yellow text-3xl md:text-4xl font-extrabold px-6 py-3 rounded-lg text-center tracking-widest inline-block";
        } else {
            plateBadge.className = "vn-plate-white text-3xl md:text-4xl font-extrabold px-6 py-3 rounded-lg text-center tracking-widest inline-block";
        }

        // Switch to result screen
        loadingSection.classList.add('hidden');
        resultSection.classList.remove('hidden');
    }

    // --- 5. Copy Plate & Reset Actions ---
    copyPlateBtn?.addEventListener('click', () => {
        const textToCopy = plateNumberText.textContent;
        navigator.clipboard.writeText(textToCopy).then(() => {
            copyToast.classList.remove('hidden');
            setTimeout(() => {
                copyToast.classList.add('hidden');
            }, 2000);
        });
    });

    resetBtn?.addEventListener('click', () => {
        resetFileInput();
        resultSection.classList.add('hidden');
        uploadSection.classList.remove('hidden');
    });
});
