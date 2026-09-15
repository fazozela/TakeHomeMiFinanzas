import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { GameDto, Move, Rule, RoundResult, Stat } from '../interfaces/game.interfaces';

@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);

  moves() { return this.http.get<Move[]>('/api/moves'); }
  createMove(name: string) { return this.http.post<Move>('/api/moves', { name }); }

  rules() { return this.http.get<Rule[]>('/api/rules'); }
  createRule(winnerMoveId: number, loserMoveId: number) {
    return this.http.post('/api/rules', { winnerMoveId, loserMoveId });
  }
  deleteRule(winnerId: number, loserId: number) {
    return this.http.delete(`/api/rules/${winnerId}/${loserId}`);
  }

  newGame(player1Name: string, player2Name: string) {
    return this.http.post<GameDto>('/api/games', { player1Name, player2Name });
  }
  playRound(gameId: number, move1Id: number, move2Id: number) {
    return this.http.post<RoundResult>(`/api/games/${gameId}/rounds`, { move1Id, move2Id });
  }

  stats() { return this.http.get<Stat[]>('/api/stats'); }
}
