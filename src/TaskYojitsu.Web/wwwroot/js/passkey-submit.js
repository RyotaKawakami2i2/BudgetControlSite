// パスキー（WebAuthn）の操作。Blazor Web App テンプレートの PasskeySubmit.razor.js を元にした（MIT）。
// 画面の HTML の中にスクリプトを書かない（CSP。詳細設計書 8.1）ため、このファイルを自サイトから配信する。
// パスキーの操作は、利用者がボタンを押したときだけ始める（画面を開いただけで OS の本人確認の画面を出さない）。
const browserSupportsPasskeys =
    typeof navigator.credentials !== 'undefined' &&
    typeof window.PublicKeyCredential !== 'undefined' &&
    typeof window.PublicKeyCredential.parseCreationOptionsFromJSON === 'function' &&
    typeof window.PublicKeyCredential.parseRequestOptionsFromJSON === 'function';

async function postForOptions(url, headers, signal) {
    const response = await fetch(url, {
        method: 'POST',
        credentials: 'same-origin',
        headers,
        signal,
    });
    if (!response.ok) {
        throw new Error('パスキーの準備に失敗しました。画面を読み込み直してください。');
    }
    return await response.json();
}

async function createCredential(headers, signal) {
    const optionsJson = await postForOptions('/account/passkey-creation-options', headers, signal);
    const options = PublicKeyCredential.parseCreationOptionsFromJSON(optionsJson);
    return await navigator.credentials.create({ publicKey: options, signal });
}

async function requestCredential(headers, signal) {
    const optionsJson = await postForOptions('/account/passkey-request-options', headers, signal);
    const options = PublicKeyCredential.parseRequestOptionsFromJSON(optionsJson);
    return await navigator.credentials.get({ publicKey: options, signal });
}

customElements.define('passkey-submit', class extends HTMLElement {
    static formAssociated = true;

    connectedCallback() {
        this.internals = this.attachInternals();
        this.attrs = {
            operation: this.getAttribute('operation'),
            name: this.getAttribute('name'),
            requestTokenName: this.getAttribute('request-token-name'),
            requestTokenValue: this.getAttribute('request-token-value'),
        };

        this.internals.form.addEventListener('submit', (event) => {
            if (event.submitter?.name === '__passkeySubmit') {
                event.preventDefault();
                this.obtainAndSubmitCredential();
            }
        });
    }

    disconnectedCallback() {
        this.abortController?.abort();
    }

    async obtainCredential(signal) {
        if (!browserSupportsPasskeys) {
            throw new Error('このブラウザはパスキーに対応していません。ブラウザを最新にしてください。');
        }

        const headers = { [this.attrs.requestTokenName]: this.attrs.requestTokenValue };
        if (this.attrs.operation === 'Create') {
            return await createCredential(headers, signal);
        }
        if (this.attrs.operation === 'Request') {
            return await requestCredential(headers, signal);
        }
        throw new Error('パスキーの操作の種類が正しくありません。');
    }

    async obtainAndSubmitCredential() {
        this.abortController?.abort();
        this.abortController = new AbortController();
        const formData = new FormData();
        try {
            const credential = await this.obtainCredential(this.abortController.signal);
            formData.append(`${this.attrs.name}.CredentialJson`, JSON.stringify(credential));
        } catch (error) {
            if (error.name === 'AbortError') {
                // 画面を移った場合などは何もしない
                return;
            }
            const message = error.name === 'NotAllowedError'
                ? 'パスキーが選ばれませんでした。'
                : error.message;
            formData.append(`${this.attrs.name}.Error`, message);
        }
        this.internals.setFormValue(formData);
        this.internals.form.submit();
    }
});
