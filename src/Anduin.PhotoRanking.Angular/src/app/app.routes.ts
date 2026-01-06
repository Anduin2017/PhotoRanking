import { Routes } from '@angular/router';
import { FeedComponent } from './components/feed/feed';
import { DiscoverComponent } from './components/discover/discover';
import { BrowserComponent } from './components/browser/browser';

import { AlbumComponent } from './components/album/album';
import { AdvancedComponent } from './components/advanced/advanced';

export const routes: Routes = [
    { path: '', redirectTo: 'feed', pathMatch: 'full' },
    { path: 'feed', component: FeedComponent },
    { path: 'discover', component: DiscoverComponent },
    { path: 'browser', component: BrowserComponent },
    { path: 'advanced', component: AdvancedComponent },
    { path: 'album/:id', component: AlbumComponent },
];
