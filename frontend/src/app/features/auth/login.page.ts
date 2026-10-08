import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ButtonDirective } from 'primeng/button';
import { InputText } from 'primeng/inputtext';
import { Message } from 'primeng/message';
import { Password } from 'primeng/password';

import { ApiError } from '../../core/api/api-error';
import { AuthApi } from '../../core/api/auth.api';
import { DemoAccount, PublicConfig } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { ERROR_CODES } from '../../core/constants/error-codes';
import { ROUTE_PATHS } from '../../core/constants/routes';

const INVALID_CREDENTIALS_MESSAGE = 'Email or password is incorrect. Check them and try again.';
const NETWORK_MESSAGE = 'We could not reach the server. Check your connection and try again.';
const LOCKED_MESSAGE = 'Too many attempts. Try again in a few minutes.';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, ButtonDirective, InputText, Message, Password],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .wrap {
      display: flex;
      justify-content: center;
      padding: var(--space-3xl) var(--space-md) var(--space-lg);
    }
    .card {
      width: 400px;
      max-width: 100%;
      box-sizing: border-box;
      background: #ffffff;
      border: 1px solid var(--p-content-border-color, #d8e2ee);
      border-radius: 8px;
      padding: var(--space-lg);
    }
    .card.wide {
      width: 640px;
    }
    h1 {
      margin: 0 0 var(--space-xs);
    }
    .helper {
      margin: 0 0 var(--space-lg);
      color: var(--p-text-muted-color, #5b6b7d);
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: var(--space-xs);
      margin-bottom: var(--space-md);
    }
    .field-error {
      color: var(--p-red-600, #b42318);
      font-size: 12px;
    }
    button[pButton] {
      width: 100%;
    }
    .errors {
      margin-bottom: var(--space-md);
    }
    .divider {
      display: flex;
      align-items: center;
      gap: var(--space-sm);
      margin: var(--space-lg) 0 var(--space-md);
      color: var(--p-text-muted-color, #5b6b7d);
      font-size: 12px;
      font-weight: 600;
    }
    .divider::before,
    .divider::after {
      content: '';
      flex: 1;
      border-top: 1px solid var(--p-content-border-color, #d8e2ee);
    }
    .demo-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: var(--space-sm);
    }
    @media (max-width: 767px) {
      .wrap {
        padding-top: var(--space-3xl);
      }
      .demo-grid {
        grid-template-columns: repeat(2, 1fr);
      }
    }
    .demo-card {
      text-align: left;
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: var(--space-sm) var(--space-md);
      min-height: 44px;
      border: 1px solid var(--p-content-border-color, #d8e2ee);
      border-radius: 6px;
      background: #ffffff;
      color: inherit;
      font: inherit;
      cursor: pointer;
    }
    .demo-card:hover:not(:disabled) {
      background: var(--p-highlight-background, #eaf1f9);
    }
    .demo-card:disabled {
      cursor: default;
      opacity: 0.6;
    }
    .demo-card.selected {
      border: 2px solid var(--p-primary-color, #2f6db8);
      padding: calc(var(--space-sm) - 1px) calc(var(--space-md) - 1px);
    }
    .demo-role {
      font-weight: 600;
    }
    .demo-name,
    .demo-desc,
    .note {
      font-size: 12px;
      color: var(--p-text-muted-color, #5b6b7d);
    }
    .note {
      margin: var(--space-md) 0 0;
    }
  `,
  template: `
    <div class="wrap">
      <main class="card" [class.wide]="config()?.demoMode">
        <h1 class="text-display">Sign in to AdminDesk</h1>
        <p class="helper">Use your work email and password</p>

        @if (errorMessage(); as message) {
          <div class="errors" role="alert">
            <p-message severity="error">{{ message }}</p-message>
          </div>
        }

        <form [formGroup]="form" (ngSubmit)="onSubmit()" novalidate>
          <div class="field">
            <label class="text-label" for="login-email">Email</label>
            <input
              pInputText
              id="login-email"
              type="email"
              formControlName="email"
              autocomplete="username"
              [fluid]="true"
            />
            @if (showError('email')) {
              <span class="field-error">Enter your work email</span>
            }
          </div>
          <div class="field">
            <label class="text-label" for="login-password">Password</label>
            <p-password
              inputId="login-password"
              formControlName="password"
              [feedback]="false"
              [toggleMask]="true"
              autocomplete="current-password"
              [fluid]="true"
            />
            @if (showError('password')) {
              <span class="field-error">Enter your password</span>
            }
          </div>
          <button
            pButton
            type="submit"
            label="Sign in"
            class="w-full"
            [loading]="submitting()"
            [disabled]="submitting()"
          ></button>
        </form>

        @if (config(); as cfg) {
          @if (cfg.demoMode && cfg.demoAccounts.length > 0) {
            <div class="divider">Try a demo role</div>
            <div class="demo-grid">
              @for (account of cfg.demoAccounts; track account.email) {
                <button
                  type="button"
                  class="demo-card"
                  [class.selected]="selectedEmail() === account.email"
                  [disabled]="submitting()"
                  [attr.aria-pressed]="selectedEmail() === account.email"
                  (click)="onDemoCard(account, cfg.demoPassword)"
                >
                  <span class="demo-role">{{ account.role }}</span>
                  <span class="demo-name">{{ account.name }}</span>
                  <span class="demo-desc">{{ account.description }}</span>
                </button>
              }
            </div>
            <p class="note">Demo accounts use sample data and a shared practice password.</p>
          }
        }
      </main>
    </div>
  `,
})
export class LoginPage implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly authApi = inject(AuthApi);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly config = signal<PublicConfig | null>(null);
  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly selectedEmail = signal<string | null>(null);
  private readonly submitted = signal(false);

  protected readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  ngOnInit(): void {
    if (this.authService.isAuthenticated()) {
      void this.router.navigate(['/', ROUTE_PATHS.Dashboard]);
      return;
    }
    this.authApi.publicConfig().subscribe({
      next: (cfg) => this.config.set(cfg),
      error: () => this.config.set(null),
    });
  }

  protected showError(name: 'email' | 'password'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || this.submitted());
  }

  protected onSubmit(): void {
    this.submitted.set(true);
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting()) return;
    const { email, password } = this.form.getRawValue();
    this.signIn(email, password);
  }

  protected onDemoCard(account: DemoAccount, demoPassword: string | null): void {
    if (this.submitting() || !demoPassword) return;
    this.selectedEmail.set(account.email);
    this.form.setValue({ email: account.email, password: demoPassword });
    this.signIn(account.email, demoPassword);
  }

  /** The only sign-in path: the form and the demo cards both end up here. */
  private signIn(email: string, password: string): void {
    this.errorMessage.set(null);
    this.submitting.set(true);
    this.form.disable({ emitEvent: false });
    this.authService.login(email, password).subscribe({
      next: () => {
        const returnUrl = this.authService.sanitizeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
        if (returnUrl) {
          void this.router.navigateByUrl(returnUrl);
        } else {
          void this.router.navigate(['/', ROUTE_PATHS.Dashboard]);
        }
      },
      error: (err: unknown) => {
        this.submitting.set(false);
        this.form.enable({ emitEvent: false });
        this.errorMessage.set(this.messageFor(err));
      },
    });
  }

  private messageFor(err: unknown): string {
    if (err instanceof ApiError) {
      if (err.status === 0) return NETWORK_MESSAGE;
      if (err.code === ERROR_CODES.AccountLocked) return LOCKED_MESSAGE;
      if (err.code === ERROR_CODES.InvalidCredentials) return INVALID_CREDENTIALS_MESSAGE;
    }
    return err instanceof ApiError && err.status >= 500 ? 'Something went wrong on our side. Try again.' : INVALID_CREDENTIALS_MESSAGE;
  }
}
