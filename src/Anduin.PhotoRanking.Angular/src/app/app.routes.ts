import { Routes, UrlSegment } from '@angular/router';

export function browserMatcher(url: UrlSegment[]) {
    if (url.length > 0 && url[0].path === 'browser') {
        return {
            consumed: url,
            posParams: {
                path: new UrlSegment(url.slice(1).map(s => s.path).join('/'), {})
            }
        };
    }
    return null;
}

export const routes: Routes = [
    { path: '', redirectTo: 'feed', pathMatch: 'full' },
    {
        path: 'feed',
        loadComponent: () => import('./components/feed/feed').then(m => m.FeedComponent)
    },
    {
        path: 'discover',
        loadComponent: () => import('./components/discover/discover').then(m => m.DiscoverComponent)
    },
    {
        matcher: browserMatcher,
        loadComponent: () => import('./components/browser/browser').then(m => m.BrowserComponent)
    },
    {
        path: 'advanced',
        loadComponent: () => import('./components/advanced/advanced').then(m => m.AdvancedComponent)
    },
    {
        path: 'album/:id',
        loadComponent: () => import('./components/album/album').then(m => m.AlbumComponent)
    },
    {
        path: 'album/:id/dedup',
        loadComponent: () => import('./components/album/album-dedup-preview/album-dedup-preview')
            .then(m => m.AlbumDedupPreviewComponent)
    },
    {
        path: 'similar/:id',
        loadComponent: () => import('./components/similar/similar').then(m => m.SimilarComponent)
    },
    {
        path: 'search',
        loadComponent: () => import('./components/search/search').then(m => m.SearchComponent)
    },
];
