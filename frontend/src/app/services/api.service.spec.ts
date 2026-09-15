import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Api } from './api.service';

describe('Api', () => {
  let api: Api;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(Api);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('uses relative urls so the proxy resolves the origin', () => {
    api.moves().subscribe();
    http.expectOne('/api/moves').flush([]);
  });

  it('sends both moves in a single round request', () => {
    api.playRound(7, 1, 3).subscribe();

    const req = http.expectOne('/api/games/7/rounds');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ move1Id: 1, move2Id: 3 });
    req.flush({});
  });

  it('deletes a rule by its composite key', () => {
    api.deleteRule(2, 1).subscribe();

    const req = http.expectOne('/api/rules/2/1');
    expect(req.request.method).toBe('DELETE');
    req.flush({});
  });
});
