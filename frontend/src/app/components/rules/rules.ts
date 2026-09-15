import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { Api } from '../../services/api.service';
import { Move, Rule } from '../../interfaces/game.interfaces';

@Component({
  selector: 'app-rules',
  imports: [FormsModule],
  templateUrl: './rules.html',
})
export default class Rules implements OnInit {
  private api = inject(Api);

  moves = signal<Move[]>([]);
  rules = signal<Rule[]>([]);
  error = signal('');

  newMoveName = '';
  winnerId = 0;
  loserId = 0;

  ngOnInit() { this.load(); }

  addMove() {
    const name = this.newMoveName.trim();
    if (!name) return;

    this.error.set('');
    this.api.createMove(name).subscribe({
      next: () => { this.newMoveName = ''; this.load(); },
      error: err => this.error.set(err.error || 'No se pudo crear el movimiento.'),
    });
  }

  addRule() {
    this.error.set('');
    this.api.createRule(this.winnerId, this.loserId).subscribe({
      next: () => this.load(),
      error: err => this.error.set(err.error || 'No se pudo crear la regla.'),
    });
  }

  removeRule(rule: Rule) {
    this.error.set('');
    this.api.deleteRule(rule.winnerMoveId, rule.loserMoveId).subscribe({
      next: () => this.load(),
      error: err => this.error.set(err.error || 'No se pudo eliminar la regla.'),
    });
  }

  private load() {
    forkJoin({ moves: this.api.moves(), rules: this.api.rules() }).subscribe(data => {
      this.moves.set(data.moves);
      this.rules.set(data.rules);
      this.winnerId = data.moves[0]?.id ?? 0;
      this.loserId = data.moves[1]?.id ?? 0;
    });
  }
}
