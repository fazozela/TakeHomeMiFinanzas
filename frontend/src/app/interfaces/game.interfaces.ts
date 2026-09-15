export interface Move { id: number; name: string; }
export interface Rule { winnerMoveId: number; winnerName: string; loserMoveId: number; loserName: string; }
export interface GameDto { id: number; player1Name: string; player2Name: string; }
export interface Stat { name: string; gamesWon: number; }
export interface RoundResult {
  number: number;
  roundWinner: string | null;
  player1Wins: number;
  player2Wins: number;
  gameWinner: string | null;
}
