import { Routes, UrlSegment } from '@angular/router';
import { FeedComponent } from './components/feed/feed';
import { DiscoverComponent } from './components/discover/discover';
import { BrowserComponent } from './components/browser/browser';

import { AlbumComponent } from './components/album/album';
import { AdvancedComponent } from './components/advanced/advanced';
import { SimilarComponent } from './components/similar/similar';
import { SearchComponent } from './components/search/search';

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

import { AlbumDedupPreviewComponent } from './components/album/album-dedup-preview/album-dedup-preview';

export const routes: Routes = [
    { path: '', redirectTo: 'feed', pathMatch: 'full' },
    { path: 'feed', component: FeedComponent },
    { path: 'discover', component: DiscoverComponent },
    { matcher: browserMatcher, component: BrowserComponent },
    { path: 'advanced', component: AdvancedComponent },
    { path: 'album/:id', component: AlbumComponent },
    { path: 'album/:id/dedup', component: AlbumDedupPreviewComponent },
    { path: 'similar/:id', component: SimilarComponent },
    { path: 'search', component: SearchComponent },
];
