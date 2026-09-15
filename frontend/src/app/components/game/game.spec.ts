import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import Game from './game';

describe('Game', () => {
  let fixture: ComponentFixture<Game>;
  let game: Game;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Game],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(Game);
    game = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function startGame() {
    game.player1 = 'Fazo';
    game.player2 = 'Rival';
    game.start();
    http.expectOne('/api/games').flush({ id: 1, player1Name: 'Fazo', player2Name: 'Rival' });
    http.expectOne('/api/moves').flush([
      { id: 1, name: 'Rock' },
      { id: 3, name: 'Scissors' },
    ]);
  }

  it('asks player 1 first once the game starts', () => {
    startGame();

    expect(game.phase()).toBe('p1');
    expect(game.currentPlayer()).toBe('Fazo');
  });

  it('keeps player 1 move private until player 2 has chosen', () => {
    startGame();

    game.selectedMoveId = 1;
    game.ok();

    http.expectNone('/api/games/1/rounds');
    expect(game.phase()).toBe('p2');
    expect(game.currentPlayer()).toBe('Rival');
  });

  it('sends both moves together and takes the score from the server', () => {
    startGame();

    game.selectedMoveId = 1;
    game.ok();
    game.selectedMoveId = 3;
    game.ok();

    const req = http.expectOne('/api/games/1/rounds');
    expect(req.request.body).toEqual({ move1Id: 1, move2Id: 3 });

    req.flush({ number: 1, roundWinner: 'Fazo', player1Wins: 1, player2Wins: 0, gameWinner: null });

    expect(game.score()).toEqual({ p1: 1, p2: 0 });
    expect(game.history()).toEqual([{ number: 1, winner: 'Fazo' }]);
    expect(game.roundNumber()).toBe(2);
    expect(game.phase()).toBe('p1');
  });

  it('shows the winner screen when the server reports a game winner', () => {
    startGame();

    game.selectedMoveId = 1;
    game.ok();
    game.selectedMoveId = 3;
    game.ok();

    http.expectOne('/api/games/1/rounds').flush({
      number: 3, roundWinner: 'Fazo', player1Wins: 3, player2Wins: 0, gameWinner: 'Fazo',
    });

    expect(game.phase()).toBe('done');
    expect(game.winner()).toBe('Fazo');
  });

  it('records a tie as a round without winner', () => {
    startGame();

    game.selectedMoveId = 1;
    game.ok();
    game.selectedMoveId = 1;
    game.ok();

    http.expectOne('/api/games/1/rounds').flush({
      number: 1, roundWinner: null, player1Wins: 0, player2Wins: 0, gameWinner: null,
    });

    expect(game.history()).toEqual([{ number: 1, winner: null }]);
    expect(game.phase()).toBe('p1');
  });
});
