import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { map, switchMap } from 'rxjs';
import { Api } from '../../services/api.service';
import { Move } from '../../interfaces/game.interfaces';

type Phase = 'setup' | 'p1' | 'p2' | 'done';

@Component({
  selector: 'app-game',
  imports: [FormsModule],
  templateUrl: './game.html',
})
export default class Game {
  private api = inject(Api);

  phase = signal<Phase>('setup');
  error = signal('');

  player1 = '';
  player2 = '';
  selectedMoveId = 0;

  moves = signal<Move[]>([]);
  names = signal({ p1: '', p2: '' });
  score = signal({ p1: 0, p2: 0 });
  history = signal<{ number: number; winner: string | null }[]>([]);
  roundNumber = signal(1);
  winner = signal('');

  currentPlayer = computed(() => this.phase() === 'p1' ? this.names().p1 : this.names().p2);

  private gameId = 0;
  private move1Id = 0;

  start() {
    this.error.set('');
    this.api.newGame(this.player1, this.player2).pipe(
      switchMap(game => this.api.moves().pipe(map(moves => ({ game, moves }))))
    ).subscribe({
      next: ({ game, moves }) => {
        this.gameId = game.id;
        this.names.set({ p1: game.player1Name, p2: game.player2Name });
        this.moves.set(moves);
        this.score.set({ p1: 0, p2: 0 });
        this.history.set([]);
        this.roundNumber.set(1);
        this.resetSelection();
        this.phase.set('p1');
      },
      error: err => this.error.set(err.error || 'No se pudo iniciar la partida.'),
    });
  }

  ok() {
    if (this.phase() === 'p1') {
      this.move1Id = this.selectedMoveId;
      this.resetSelection();
      this.phase.set('p2');
      return;
    }

    this.api.playRound(this.gameId, this.move1Id, this.selectedMoveId).subscribe({
      next: result => {
        this.history.update(h => [...h, { number: result.number, winner: result.roundWinner }]);
        this.score.set({ p1: result.player1Wins, p2: result.player2Wins });
        this.resetSelection();

        if (result.gameWinner) {
          this.winner.set(result.gameWinner);
          this.phase.set('done');
        } else {
          this.roundNumber.set(result.number + 1);
          this.phase.set('p1');
        }
      },
      error: err => this.error.set(err.error || 'No se pudo jugar la ronda.'),
    });
  }

  playAgain() {
    this.player1 = '';
    this.player2 = '';
    this.error.set('');
    this.phase.set('setup');
  }

  private resetSelection() {
    this.selectedMoveId = this.moves()[0]?.id ?? 0;
  }
}
