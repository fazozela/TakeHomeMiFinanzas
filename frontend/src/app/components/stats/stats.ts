import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Api } from '../../services/api.service';
import { Stat } from '../../interfaces/game.interfaces';

@Component({
  selector: 'app-stats',
  imports: [],
  templateUrl: './stats.html',
})
export default class Stats {
  private api = inject(Api);
  stats = toSignal(this.api.stats(), { initialValue: [] as Stat[] });
}
