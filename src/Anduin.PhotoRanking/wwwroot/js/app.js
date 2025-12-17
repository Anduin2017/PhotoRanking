// API基础URL
const API_BASE = '/api';

// 全局状态
const state = {
    currentPage: 'feed',
    previousPage: 'feed', // 上一个页面，用于相册返回
    currentPhotoId: null,
    currentAlbumId: null,
    viewerContext: 'feed', // feed, discover, album
    albumPhotoIds: [], // 当前相册中的所有照片ID列表
    feedPhotoIds: [], // Feed中的照片ID列表（用于左右键切换）
    discoverPhotoIds: [], // Discover中的照片ID列表（用于左右键切换）
    currentPhotoIndex: -1, // 当前照片在列表中的索引
};

// 工具函数
function showToast(message, type = 'success') {
    const container = document.getElementById('toast-container');
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.textContent = message;
    container.appendChild(toast);
    
    setTimeout(() => {
        toast.style.opacity = '0';
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

function formatScore(score) {
    return score != null ? score.toFixed(2) : '-';
}

// ========== URL路由功能 ==========

// 从URL更新状态
function updateStateFromURL() {
    const hash = window.location.hash.slice(1); // 移除 #
    if (!hash) {
        switchPage('feed');
        return;
    }
    
    // 解析hash - 使用正则表达式而不是简单split，支持编码的albumId
    const albumMatch = hash.match(/^album\/([^\/]+)(?:\/photo\/(\d+))?$/);
    if (albumMatch) {
        // 格式: #album/encoded-album-id 或 #album/encoded-album-id/photo/456
        const albumId = decodeURIComponent(albumMatch[1]);
        viewAlbum(albumId).then(() => {
            // 如果URL中有photo参数，自动打开照片查看器
            if (albumMatch[2]) {
                const photoId = parseInt(albumMatch[2]);
                state.viewerContext = 'album'; // 确保context正确
                openPhotoViewer(photoId);
            }
        });
        return;
    }
    
    // 解析discover路由: #discover/{mode} 或 #discover/{mode}/photo/{photoId}
    const discoverMatch = hash.match(/^discover(?:\/(waiting|all|top))?(?:\/photo\/(\d+))?$/);
    if (discoverMatch) {
        const mode = discoverMatch[1] || 'waiting';
        switchPage('discover');
        switchDiscoverMode(mode);
        // 如果URL中有photo参数，自动打开照片查看器
        if (discoverMatch[2]) {
            const photoId = parseInt(discoverMatch[2]);
            state.viewerContext = 'discover';
            // 延迟打开，等待页面加载完成
            setTimeout(() => openPhotoViewer(photoId), 100);
        }
        return;
    }
    
    // 其他简单页面路由
    if (['feed', 'advanced'].includes(hash)) {
        switchPage(hash);
    } else {
        // 无效hash，回到主页
        window.location.hash = 'feed';
    }
}

// 更新URL（不触发hashchange）
function updateURL(page, params = {}) {
    let hash = page;
    if (params.albumId) {
        // 对albumId进行URL编码，支持包含斜杠的嵌套路径
        hash = `album/${encodeURIComponent(params.albumId)}`;
    } else if (params.mode) {
        hash = `${page}/${params.mode}`;
    }
    
    // 只在hash真正改变时才更新
    if (window.location.hash !== `#${hash}`) {
        window.location.hash = hash;
    }
}

function getImageUrl(filePath) {
    return `${API_BASE}/images/${filePath}`;
}

// 导航功能
function switchPage(pageName) {
    // 更新导航标签
    document.querySelectorAll('.nav-tab').forEach(tab => {
        tab.classList.toggle('active', tab.dataset.page === pageName);
    });
    
    // 更新页面显示
    document.querySelectorAll('.page').forEach(page => {
        page.classList.remove('active');
    });
    
    const targetPage = document.getElementById(`${pageName}-page`);
    if (targetPage) {
        targetPage.classList.add('active');
        // 保存上一个页面（用于相册返回）
        state.previousPage = state.currentPage;
        state.currentPage = pageName;
    }
    
    // 加载对应页面内容
    switch (pageName) {
        case 'feed':
            loadFeed();
            break;
        case 'discover':
            loadDiscover();
            break;
        case 'advanced':
            loadAdvanced();
            break;
    }
    
    // 更新URL
    updateURL(pageName);
}


// Feed分页状态
let feedCurrentPage = 1;
let feedIsLoading = false;
let feedHasMore = true;

// 加载首页Feed
async function loadFeed() {
    const container = document.getElementById('feed-container');
    container.innerHTML = '<div class="loading-spinner"></div>';
    
    // 重置状态
    feedCurrentPage = 1;
    feedIsLoading = false;
    feedHasMore = true;
    state.feedPhotoIds = [];
    
    // 加载第一页
    await loadMoreFeed();
}

async function loadMoreFeed() {
    if (feedIsLoading || !feedHasMore) return;
    
    feedIsLoading = true;
    const container = document.getElementById('feed-container');
    
    // 添加加载指示器到底部
    let loadingIndicator = document.getElementById('feed-loading-indicator');
    if (!loadingIndicator) {
        loadingIndicator = document.createElement('div');
        loadingIndicator.id = 'feed-loading-indicator';
        loadingIndicator.className = 'loading-indicator';
        loadingIndicator.innerHTML = '<div class="loading-spinner"></div><p>加载中...</p>';
        container.appendChild(loadingIndicator);
    }
    loadingIndicator.style.display = 'flex';
    
    try {
        const pageSize = 20;
        const response = await fetch(`${API_BASE}/photos/feed?page=${feedCurrentPage}&pageSize=${pageSize}`);
        const photos = await response.json();
        
        if (feedCurrentPage === 1 && photos.length === 0) {
            container.innerHTML = '<p style="text-align: center; color: var(--text-secondary);">暂无照片，请先运行数据同步</p>';
            feedHasMore = false;
            feedIsLoading = false;
            // 隐藏加载指示器
            if (loadingIndicator) {
                loadingIndicator.style.display = 'none';
            }
            return;
        }
        
        // 清空loading spinner（只在第一页）
        if (feedCurrentPage === 1) {
            container.innerHTML = '';
        }
        
        // 移除加载指示器，准备添加照片
        if (loadingIndicator && loadingIndicator.parentNode) {
            loadingIndicator.remove();
        }
        
        // 添加照片到Feed
        photos.forEach(photo => {
            const card = createFeedCard(photo);
            container.appendChild(card);
            state.feedPhotoIds.push(photo.id); // 保存ID用于导航
        });
        
        // 重新添加加载指示器到底部（如果还有更多）
        if (photos.length >= pageSize) {
            loadingIndicator = document.createElement('div');
            loadingIndicator.id = 'feed-loading-indicator';
            loadingIndicator.className = 'loading-indicator';
            loadingIndicator.innerHTML = '<div class="loading-spinner"></div><p>加载中...</p>';
            loadingIndicator.style.display = 'none'; // 初始隐藏
            container.appendChild(loadingIndicator);
        }
        
        // 更新状态
        feedCurrentPage++;
        feedIsLoading = false;
        
        // 如果返回的照片少于请求数量，说明没有更多了
        if (photos.length < pageSize) {
            feedHasMore = false;
            // 隐藏加载指示器
            if (loadingIndicator) {
                loadingIndicator.style.display = 'none';
            }
        }
        
    } catch (error) {
        console.error('Error loading feed:', error);
        if (feedCurrentPage === 1) {
            container.innerHTML = '<p style="text-align: center; color: red;">加载失败</p>';
        }
        feedIsLoading = false;
        feedHasMore = false;
        
        // 隐藏加载指示器
        if (loadingIndicator) {
            loadingIndicator.style.display = 'none';
        }
    }
}

function createFeedCard(photo) {
    const card = document.createElement('div');
    card.className = 'feed-card';
    card.dataset.photoId = photo.id;
    
    card.innerHTML = `
        <img src="${getImageUrl(photo.filePath)}" alt="" class="feed-card-image" loading="lazy">
        <div class="feed-card-info">
            <div class="feed-card-album">
                <a href="#" class="album-link" data-album-id="${photo.albumId}">
                    <span>${photo.album?.name || photo.albumId}</span>
                    <span style="font-size: 0.8em; opacity: 0.6; margin-left: 6px; font-weight: normal;">${photo.albumId}</span>
                </a>
            </div>
            <div class="feed-card-rating">
                <div class="rating-prompt-inline">为这张照片打分</div>
                <div class="rating-buttons-inline">
                    <button class="rating-btn-inline" data-score="0">0</button>
                    <button class="rating-btn-inline" data-score="1">1</button>
                    <button class="rating-btn-inline" data-score="2">2</button>
                    <button class="rating-btn-inline" data-score="3">3</button>
                    <button class="rating-btn-inline" data-score="4">4</button>
                    <button class="rating-btn-inline" data-score="5">5</button>
                </div>
            </div>
        </div>
    `;
    
    // 点击图片区域打开查看器
    card.querySelector('.feed-card-image').addEventListener('click', () => {
        state.viewerContext = 'feed';
        openPhotoViewer(photo.id);
    });
    
    // 点击相册链接
    card.querySelector('.album-link').addEventListener('click', (e) => {
        e.preventDefault();
        viewAlbum(photo.albumId);
    });
    
    // 添加评分按钮事件
    card.querySelectorAll('.rating-btn-inline').forEach(btn => {
        btn.addEventListener('click', async (e) => {
            e.stopPropagation();
            const score = parseInt(btn.dataset.score);
            await rateFeedPhoto(photo.id, score, card);
        });
    });
    
    return card;
}

// 加载探索页
let discoverCurrentMode = 'waiting'; // 当前模式
let discoverCurrentPage = 1; // 当前页码
let discoverIsLoading = false; // 是否正在加载
let discoverHasMore = true; // 是否还有更多

async function loadDiscover() {
    // 重置状态
    discoverCurrentPage = 1;
    discoverHasMore = true;
    
    const grid = document.getElementById('discover-grid');
    grid.innerHTML = '<div class="loading-spinner"></div>';
    
    // 加载第一页
    await loadDiscoverPhotos();
}

async function loadDiscoverPhotos() {
    if (discoverIsLoading || !discoverHasMore) return;
    
    discoverIsLoading = true;
    const loadingMore = document.getElementById('discover-loading-more');
    
    if (discoverCurrentPage > 1) {
        loadingMore.style.display = 'flex';
    }
    
    try {
        const response = await fetch(`${API_BASE}/photos/discover?mode=${discoverCurrentMode}&page=${discoverCurrentPage}&pageSize=30`);
        const photos = await response.json();
        
        const grid = document.getElementById('discover-grid');
        
        // 第一页清空grid和ID列表
        if (discoverCurrentPage === 1) {
            grid.innerHTML = '';
            state.discoverPhotoIds = []; // 清空ID列表
        }
        
        if (photos.length === 0) {
            discoverHasMore = false;
            if (discoverCurrentPage === 1) {
                grid.innerHTML = '<p style="text-align: center; grid-column: 1 / -1; color: var(--text-secondary);">暂无照片</p>';
            }
        } else {
            // 追加照片和ID
            photos.forEach(photo => {
                grid.appendChild(createDiscoverItem(photo));
                state.discoverPhotoIds.push(photo.id); // 保存ID
            });
            
            discoverCurrentPage++;
            
            // 如果返回的照片少于请求的数量，说明没有更多了
            if (photos.length < 30) {
                discoverHasMore = false;
            }
        }
        
    } catch (error) {
        console.error('Error loading discover:', error);
        const grid = document.getElementById('discover-grid');
        grid.innerHTML = '<p style="text-align: center; grid-column: 1 / -1; color: red;">加载失败</p>';
    } finally {
        discoverIsLoading = false;
        loadingMore.style.display = 'none';
    }
}

// 切换探索模式
function switchDiscoverMode(mode) {
    discoverCurrentMode = mode;
    
    // 更新按钮状态
    document.querySelectorAll('.mode-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.mode === mode);
    });
    
    // 更新URL
    window.history.replaceState(null, '', `#discover/${mode}`);
    
    // 重新加载
    loadDiscover();
}

function createDiscoverItem(photo) {
    const item = document.createElement('div');
    item.className = 'discover-item';
    
    item.innerHTML = `
        <img src="${getImageUrl(photo.filePath)}" alt="" loading="lazy">
        <div class="discover-item-overlay">
            <div class="discover-item-score">分数: ${formatScore(photo.overallScore)}</div>
        </div>
    `;
    
    item.addEventListener('click', () => {
        state.viewerContext = 'discover';
        openPhotoViewer(photo.id);
    });
    
    return item;
}

// 加载高级统计页
let advancedLoadedCounts = {
    albumsByScore: 10,
    albumsByKnownRate: 10,
    photosByScore: 20,
    photosByKnownness: 20
};

async function loadAdvanced() {
    const container = document.getElementById('advanced-container');
    container.innerHTML = '<div class="loading-spinner"></div>';
    
    // 重置加载计数
    advancedLoadedCounts = {
        albumsByScore: 10,
        albumsByKnownRate: 10,
        photosByScore: 20,
        photosByKnownness: 20
    };
    
    try {
        const response = await fetch(`${API_BASE}/photos/stats/top`);
        const data = await response.json();
        
        container.innerHTML = '';
        
        // 1. 相册分最高的相册
        createStatSection(container, 'albumScore', '相册分最高的相册', data.topAlbumsByScore, 
            (album) => createAlbumStatCard(album, '相册分', album.albumScore, ''));
        
        // 2. 已知率最高的相册
        createStatSection(container, 'albumKnownRate', '已知率最高的相册', data.topAlbumsByKnownRate, 
            (album) => createAlbumStatCard(album, '已知率', album.knownRate, '%'));
        
        // 3. 整体分最高的照片
        createStatSection(container, 'photoScore', '整体分最高的照片', data.topPhotosByScore, 
            (photo) => createPhotoGridItem(photo));
        
        // 4. 已知性最高的照片
        createStatSection(container, 'photoKnownness', '已知性最高的照片', data.topPhotosByKnownness, 
            (photo) => createPhotoGridItem(photo));
        
    } catch (error) {
        console.error('Error loading advanced stats:', error);
        container.innerHTML = '<p style="text-align: center; color: red;">加载失败</p>';
    }
}

function createStatSection(container, sectionId, title, items, createItemFunc) {
    const section = document.createElement('div');
    section.className = 'stat-section';
    section.id = `section-${sectionId}`;
    
    section.innerHTML = `
        <h2 class="stat-section-title">${title}</h2>
        <div class="${sectionId.includes('photo') ? 'photo-grid' : 'stat-grid'}" id="grid-${sectionId}"></div>
        <div class="load-more-container">
            <button class="load-more-btn" data-section="${sectionId}">Load More</button>
        </div>
    `;
    
    const grid = section.querySelector(`#grid-${sectionId}`);
    items.forEach(item => {
        grid.appendChild(createItemFunc(item));
    });
    
    container.appendChild(section);
    
    // 绑定Load More按钮事件
    const loadMoreBtn = section.querySelector('.load-more-btn');
    loadMoreBtn.addEventListener('click', () => loadMoreItems(sectionId));
}

async function loadMoreItems(sectionId) {
    const loadMoreBtn = document.querySelector(`[data-section="${sectionId}"]`);
    loadMoreBtn.disabled = true;
    loadMoreBtn.textContent = 'Loading...';
    
    try {
        let endpoint, skip, take, createItemFunc;
        
        switch(sectionId) {
            case 'albumScore':
                skip = advancedLoadedCounts.albumsByScore;
                take = 5;
                endpoint = `albums/top-by-score?skip=${skip}&take=${take}`;
                createItemFunc = (album) => createAlbumStatCard(album, '相册分', album.albumScore, '');
                advancedLoadedCounts.albumsByScore += take;
                break;
            case 'albumKnownRate':
                skip = advancedLoadedCounts.albumsByKnownRate;
                take = 5;
                endpoint = `albums/top-by-knownrate?skip=${skip}&take=${take}`;
                createItemFunc = (album) => createAlbumStatCard(album, '已知率', album.knownRate, '%');
                advancedLoadedCounts.albumsByKnownRate += take;
                break;
            case 'photoScore':
                skip = advancedLoadedCounts.photosByScore;
                take = 5;
                endpoint = `photos/top-by-score?skip=${skip}&take=${take}`;
                createItemFunc = (photo) => createPhotoGridItem(photo);
                advancedLoadedCounts.photosByScore += take;
                break;
            case 'photoKnownness':
                skip = advancedLoadedCounts.photosByKnownness;
                take = 5;
                endpoint = `photos/top-by-knownness?skip=${skip}&take=${take}`;
                createItemFunc = (photo) => createPhotoGridItem(photo);
                advancedLoadedCounts.photosByKnownness += take;
                break;
        }
        
        const response = await fetch(`${API_BASE}/${endpoint}`);
        const items = await response.json();
        
        const grid = document.querySelector(`#grid-${sectionId}`);
        items.forEach(item => {
            grid.appendChild(createItemFunc(item));
        });
        
        // 如果返回的项目少于请求的数量，说明没有更多了
        if (items.length < take) {
            loadMoreBtn.style.display = 'none';
        } else {
            loadMoreBtn.disabled = false;
            loadMoreBtn.textContent = 'Load More';
        }
        
    } catch (error) {
        console.error('Error loading more items:', error);
        loadMoreBtn.disabled = false;
        loadMoreBtn.textContent = 'Load More';
        showToast('加载失败', 'error');
    }
}

function createAlbumStatCard(album, label, value, suffix) {
    const card = document.createElement('div');
    card.className = 'stat-card album-thumbnail-card';
    
    const displayValue = typeof value === 'number' 
        ? (suffix === '%' ? (value * 100).toFixed(1) : value.toFixed(2))
        : '-';
    
    // 设置背景图（如果有）
    if (album.thumbnailPath) {
        card.style.backgroundImage = `url('${getImageUrl(album.thumbnailPath)}')`;
    }
    
    card.innerHTML = `
        <div class="album-card-overlay"></div>
        <div class="album-card-content">
            <div class="stat-card-title">
                ${album.name}
                <div style="font-size: 0.7em; opacity: 0.6; font-weight: normal; margin-top: 2px; word-break: break-all;">${album.albumId}</div>
            </div>
            <div class="album-card-stats">
                <div class="stat-item">
                    <span class="stat-label">${label}</span>
                    <span class="stat-value">${displayValue}${suffix}</span>
                </div>
                <div class="stat-item">
                    <span class="stat-label">照片</span>
                    <span class="stat-value">${album.photoCount}</span>
                </div>
            </div>
        </div>
    `;
    
    card.addEventListener('click', () => {
        viewAlbum(album.albumId);
    });
    
    return card;
}

function createPhotoGridItem(photo) {
    const item = document.createElement('div');
    item.className = 'photo-grid-item';
    
    item.innerHTML = `<img src="${getImageUrl(photo.filePath)}" alt="" loading="lazy">`;
    
    item.addEventListener('click', () => {
        // 判断当前是否在相册页面
        if (document.getElementById('album-page').classList.contains('active')) {
            state.viewerContext = 'album';
        } else {
            state.viewerContext = 'advanced';
        }
        openPhotoViewer(photo.id);
    });
    
    return item;
}

// 查看相册
async function viewAlbum(albumId) {
    state.currentAlbumId = albumId;
    
    // 保存上一个页面（用于返回）
    // 如果当前不在相册页面，则保存当前页面作为previous
    if (state.currentPage !== 'album') {
        state.previousPage = state.currentPage;
    }
    state.currentPage = 'album'; // 更新当前页面状态
    
    // 切换到相册页面
    document.querySelectorAll('.page').forEach(page => page.classList.remove('active'));
    document.getElementById('album-page').classList.add('active');
    
    const titleEl = document.getElementById('album-title');
    const statsEl = document.getElementById('album-stats');
    const photosContainer = document.getElementById('album-photos-container');
    
    photosContainer.innerHTML = '<div class="loading-spinner"></div>';
    
    try {
        // 对albumId进行URL编码，支持包含斜杠的路径
        const response = await fetch(`${API_BASE}/albums/${encodeURIComponent(albumId)}`);
        const data = await response.json();
        
        const album = data.album;
        const photos = data.photos;
        
        titleEl.innerHTML = `
            ${album.name}
            <div style="font-size: 0.4em; opacity: 0.6; font-weight: normal; margin-top: 5px;">${album.albumId}</div>
        `;
        
        statsEl.innerHTML = `
            <div class="stat-badge">
                <span class="stat-badge-label">相册分</span>
                <span class="stat-badge-value">${formatScore(album.albumScore)}</span>
            </div>
            <div class="stat-badge">
                <span class="stat-badge-label">已知率</span>
                <span class="stat-badge-value">${(album.knownRate * 100).toFixed(1)}%</span>
            </div>
            <div class="stat-badge">
                <span class="stat-badge-label">标准差</span>
                <span class="stat-badge-value">${formatScore(album.standardDeviation)}</span>
            </div>
            <div class="stat-badge">
                <span class="stat-badge-label">照片数量</span>
                <span class="stat-badge-value">${album.photoCount}</span>
            </div>
        `;
        
        photosContainer.innerHTML = '';
        
        // 保存相册中的所有照片ID
        state.albumPhotoIds = photos.map(p => p.id);
        
        photos.forEach(photo => {
            const item = createPhotoGridItem(photo);
            photosContainer.appendChild(item);
        });
        
    } catch (error) {
        console.error('Error loading album:', error);
        photosContainer.innerHTML = '<p style="text-align: center; color: red;">加载失败</p>';
    }
    
    // 更新URL
    updateURL('album', { albumId });
}

// 打开照片查看器
async function openPhotoViewer(photoId) {
    state.currentPhotoId = photoId;
    
    // 根据context设置currentPhotoIndex
    if (state.viewerContext === 'feed') {
        state.currentPhotoIndex = state.feedPhotoIds.indexOf(photoId);
    } else if (state.viewerContext === 'album') {
        state.currentPhotoIndex = state.albumPhotoIds.indexOf(photoId);
    } else if (state.viewerContext === 'discover') {
        state.currentPhotoIndex = state.discoverPhotoIds.indexOf(photoId);
    }
    
    const viewer = document.getElementById('photo-viewer');
    viewer.classList.add('active');
    
    try {
        // 增加浏览次数
        await fetch(`${API_BASE}/photos/${photoId}/view`, { method: 'POST' });
        
        // 获取照片详情
        const response = await fetch(`${API_BASE}/photos/${photoId}`);
        const data = await response.json();
        
        // 更新图片
        document.getElementById('viewer-image').src = getImageUrl(data.filePath);
        
        // 更新相册信息
        viewer.querySelector('.album-name').innerHTML = `
            ${data.album?.name || data.albumId}
            <span style="font-size: 0.8em; opacity: 0.6; margin-left: 8px; font-weight: normal;">${data.albumId}</span>
        `;
        document.getElementById('viewer-album-score').textContent = data.album?.albumScore != null 
            ? formatScore(data.album.albumScore) 
            : '-';
        viewer.querySelector('.btn-view-album').onclick = () => {
            closePhotoViewer();
            viewAlbum(data.albumId);
        };
        
        // 更新统计信息
        document.getElementById('viewer-overall-score').textContent = formatScore(data.overallScore);
        document.getElementById('viewer-independent-score').textContent = data.independentScore != null ? formatScore(data.independentScore) : '-';
        document.getElementById('viewer-knownness').textContent = formatScore(data.knownness);
        document.getElementById('viewer-rating-count').textContent = data.ratingCount;
        
        // 高亮历史独立分（最后一次打的分）
        const ratingButtons = document.querySelectorAll('.rating-btn');
        ratingButtons.forEach(btn => {
            btn.classList.remove('last-rated'); // 清除之前的高亮
            
            // 如果有独立分，且该按钮对应的分数等于独立分，添加高亮
            if (data.independentScore != null && 
                parseInt(btn.dataset.score) === Math.round(data.independentScore)) {
                btn.classList.add('last-rated');
            }
        });
        
        // 更新URL根据context
        if (state.viewerContext === 'album' && state.currentAlbumId) {
            window.history.replaceState(null, '', `#album/${encodeURIComponent(state.currentAlbumId)}/photo/${photoId}`);
        } else if (state.viewerContext === 'discover') {
            window.history.replaceState(null, '', `#discover/${discoverCurrentMode}/photo/${photoId}`);
        } else if (state.viewerContext === 'feed') {
            window.history.replaceState(null, '', `#feed/photo/${photoId}`);
        }
        
    } catch (error) {
        console.error('Error opening photo viewer:', error);
        showToast('加载照片失败', 'error');
    }
}


function closePhotoViewer() {
    document.getElementById('photo-viewer').classList.remove('active');
    
    // 根据context清除URL中的photo参数
    if (state.viewerContext === 'album' && state.currentAlbumId) {
        window.history.replaceState(null, '', `#album/${encodeURIComponent(state.currentAlbumId)}`);
    } else if (state.viewerContext === 'discover') {
        window.history.replaceState(null, '', `#discover/${discoverCurrentMode}`);
    } else if (state.viewerContext === 'feed') {
        window.history.replaceState(null, '', `#feed`);
    }
    
    // 不做任何其他操作，保持当前页面状态
}

// 评分功能
async function ratePhoto(photoId, score) {
    try {
        const response = await fetch(`${API_BASE}/photos/${photoId}/rate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ score })
        });
        
        if (!response.ok) {
            throw new Error('Rating failed');
        }
        
        const updatedPhoto = await response.json();
        
        // 显示评分动画
        const buttons = document.querySelectorAll('.rating-btn');
        buttons.forEach(btn => {
            if (parseInt(btn.dataset.score) === score) {
                btn.classList.add('rated');
                setTimeout(() => btn.classList.remove('rated'), 400);
            }
        });
        
        showToast(`已评分: ${score}分`, 'success');
        
        // 更新显示的统计信息
        document.getElementById('viewer-overall-score').textContent = formatScore(updatedPhoto.overallScore);
        document.getElementById('viewer-independent-score').textContent = updatedPhoto.independentScore != null ? formatScore(updatedPhoto.independentScore) : '-';
        document.getElementById('viewer-knownness').textContent = formatScore(updatedPhoto.knownness);
        document.getElementById('viewer-rating-count').textContent = updatedPhoto.ratingCount;
        
        // 立即更新金色高亮到新打的分数
        buttons.forEach(btn => {
            btn.classList.remove('last-rated'); // 移除之前的高亮
            if (parseInt(btn.dataset.score) === score) {
                btn.classList.add('last-rated'); // 添加到新打的分数
            }
        });
        
        // 不再自动加载下一张，用户可以用左右键切换
        
    } catch (error) {
        console.error('Error rating photo:', error);
        showToast('评分失败', 'error');
    }
}

// Feed页面评分功能
async function rateFeedPhoto(photoId, score, cardElement) {
    try {
        const response = await fetch(`${API_BASE}/photos/${photoId}/rate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ score })
        });
        
        if (!response.ok) {
            throw new Error('Rating failed');
        }
        
        const updatedPhoto = await response.json();
        
        // 显示评分动画
        const buttons = cardElement.querySelectorAll('.rating-btn-inline');
        buttons.forEach(btn => {
            if (parseInt(btn.dataset.score) === score) {
                btn.classList.add('rated');
                setTimeout(() => btn.classList.remove('rated'), 400);
            }
        });
        
        // 增加浏览次数
        await fetch(`${API_BASE}/photos/${photoId}/view`, { method: 'POST' });
        
        showToast(`已评分: ${score}分`, 'success');
        
        // 立即滚动到下一张（短暂延迟以显示动画）
        setTimeout(() => {
            scrollToNextCard(cardElement);
        }, 100); // 从800ms改为100ms，几乎立即触发
        
    } catch (error) {
        console.error('Error rating photo:', error);
        showToast('评分失败', 'error');
    }
}

// 滚动到下一张照片
function scrollToNextCard(currentCard) {
    const nextCard = currentCard.nextElementSibling;
    if (nextCard && nextCard.classList.contains('feed-card')) {
        nextCard.scrollIntoView({ 
            behavior: 'smooth', 
            block: 'center'
        });
    } else {
        // 没有下一张了，重新加载Feed
        showToast('已到达最后一张，重新加载...', 'success');
        setTimeout(() => loadFeed(), 1000);
    }
}

// 加载下一张照片
async function loadNextPhoto() {
    if (state.viewerContext === 'album' || state.viewerContext === 'discover') {
        navigatePhoto(1); // 下一张
    } else {
        // 在Feed/Advanced模式下，关闭查看器
        closePhotoViewer();
    }
}

// 通用导航函数（支持左右键）
async function navigatePhoto(direction) {
    // direction: 1 = 下一张, -1 = 上一张
    
    let photoList = [];
    
    if (state.viewerContext === 'feed') {
        photoList = state.feedPhotoIds;
    } else if (state.viewerContext === 'album') {
        photoList = state.albumPhotoIds;
    } else if (state.viewerContext === 'discover') {
        // Discover模式：使用当前显示的照片列表
        photoList = state.discoverPhotoIds;
    }
    
    if (photoList.length === 0) return;
    
    // 计算下一张/上一张的索引
    let newIndex = state.currentPhotoIndex + direction;
    
    // 循环：到达末尾回到开头，到达开头回到末尾
    if (newIndex >= photoList.length) {
        newIndex = 0;
    } else if (newIndex < 0) {
        newIndex = photoList.length - 1;
    }
    
    state.currentPhotoIndex = newIndex;
    const nextPhotoId = photoList[newIndex];
    
    // 重新打开查看器显示新照片
    openPhotoViewer(nextPhotoId);
}

// 为兼容性保留的旧函数
function navigateToNextPhoto() {
    navigatePhoto(1);
}

function navigateToPreviousPhoto() {
    navigatePhoto(-1);
}

// 事件监听器
document.addEventListener('DOMContentLoaded', () => {
    // 导航标签点击
    document.querySelectorAll('.nav-tab').forEach(tab => {
        tab.addEventListener('click', () => {
            switchPage(tab.dataset.page);
        });
    });
    
    // 关闭照片查看器
    document.querySelector('.viewer-close').addEventListener('click', closePhotoViewer);
    document.querySelector('.viewer-overlay').addEventListener('click', closePhotoViewer);
    
    // 评分按钮点击
    document.querySelectorAll('.rating-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const score = parseInt(btn.dataset.score);
            ratePhoto(state.currentPhotoId, score);
        });
    });
    
    // 键盘快捷键评分和导航
    document.addEventListener('keydown', (e) => {
        const key = e.key;
        
        // 如果照片查看器打开
        if (document.getElementById('photo-viewer').classList.contains('active')) {
            // 数字键评分
            if (key >= '0' && key <= '5') {
                const score = parseInt(key);
                ratePhoto(state.currentPhotoId, score);
            } 
            // ESC键关闭
            else if (key === 'Escape') {
                closePhotoViewer();
            }
            // 左右键导航（所有context）
            else if (key === 'ArrowLeft') {
                e.preventDefault();
                navigatePhoto(-1); // 上一张
            } else if (key === 'ArrowRight') {
                e.preventDefault();
                navigatePhoto(1); // 下一张
            }
        } 
        // 如果在Feed页面，为当前视口中心的照片评分
        else if (state.currentPage === 'feed' && key >= '0' && key <= '5') {
            const score = parseInt(key);
            const feedCards = document.querySelectorAll('.feed-card');
            
            // 找到当前视口中心最近的卡片
            let closestCard = null;
            let minDistance = Infinity;
            const viewportCenter = window.innerHeight / 2;
            
            feedCards.forEach(card => {
                const rect = card.getBoundingClientRect();
                const cardCenter = rect.top + rect.height / 2;
                const distance = Math.abs(cardCenter - viewportCenter);
                
                if (distance < minDistance) {
                    minDistance = distance;
                    closestCard = card;
                }
            });
            
            if (closestCard) {
                const photoId = parseInt(closestCard.dataset.photoId);
                rateFeedPhoto(photoId, score, closestCard);
            }
        }
    });
    
    // 返回按钮
    document.getElementById('back-from-album').addEventListener('click', () => {
        // 通过URL导航，而不是直接调用switchPage，避免状态冲突
        window.location.hash = state.previousPage || 'feed';
    });
    
    // 探索模式切换按钮
    document.querySelectorAll('.mode-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            switchDiscoverMode(btn.dataset.mode);
        });
    });
    
    // Feed和Discover页面无限滚动
    window.addEventListener('scroll', () => {
        const scrollHeight = document.documentElement.scrollHeight;
        const scrollTop = document.documentElement.scrollTop;
        const clientHeight = document.documentElement.clientHeight;
        
        // 距离底部200px时触发加载
        if (scrollHeight - scrollTop - clientHeight < 200) {
            if (state.currentPage === 'feed') {
                loadMoreFeed();
            } else if (state.currentPage === 'discover') {
                loadDiscoverPhotos();
            }
        }
    });
    
    // URL路由：监听hash变化
    window.addEventListener('hashchange', () => {
        updateStateFromURL();
    });
    
    // 页面加载时从URL恢复状态（或默认加载首页）
    if (window.location.hash) {
        updateStateFromURL();
    } else {
        // 没有hash，默认加载首页
        window.location.hash = 'feed';
    }
});
