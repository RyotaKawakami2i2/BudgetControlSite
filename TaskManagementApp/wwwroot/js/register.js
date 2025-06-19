// ユーザー登録画面用JavaScript

// パスワード表示/非表示の切り替え
function togglePassword() {
    var input = document.getElementById('password');
    var icon = document.getElementById('togglePasswordIcon');
    if (input.type === 'password') {
        input.type = 'text';
        icon.classList.remove('fa-eye');
        icon.classList.add('fa-eye-slash');
    } else {
        input.type = 'password';
        icon.classList.remove('fa-eye-slash');
        icon.classList.add('fa-eye');
    }
}

// パスワード強度チェック
function checkPasswordStrength() {
    var password = document.getElementById('password').value;
    var strengthBar = document.getElementById('passwordStrength');
    
    // パスワード要件チェック
    var hasLength = password.length >= 12;
    var hasUppercase = /[A-Z]/.test(password);
    var hasLowercase = /[a-z]/.test(password);
    var hasNumber = /\d/.test(password);
    var specialChars = "!@#$%^&*(),.?\":{}|<>";
    var hasSpecial = false;
    for (var i = 0; i < specialChars.length; i++) {
        if (password.indexOf(specialChars.charAt(i)) !== -1) {
            hasSpecial = true;
            break;
        }
    }

    // 要件アイコンの更新
    updateRequirement('reqLength', hasLength);
    updateRequirement('reqUppercase', hasUppercase);
    updateRequirement('reqLowercase', hasLowercase);
    updateRequirement('reqNumber', hasNumber);
    updateRequirement('reqSpecial', hasSpecial);

    // 強度バーの更新
    var metRequirements = [hasLength, hasUppercase, hasLowercase, hasNumber, hasSpecial].filter(Boolean).length;
    
    strengthBar.className = 'password-strength';
    if (metRequirements < 3) {
        strengthBar.classList.add('strength-weak');
    } else if (metRequirements < 5) {
        strengthBar.classList.add('strength-medium');
    } else if (metRequirements === 5) {
        strengthBar.classList.add('strength-strong');
    }
}

// パスワード要件の更新
function updateRequirement(elementId, isMet) {
    var element = document.getElementById(elementId);
    if (isMet) {
        element.classList.remove('requirement-not-met');
        element.classList.add('requirement-met');
    } else {
        element.classList.remove('requirement-met');
        element.classList.add('requirement-not-met');
    }
}

// 権限説明の表示
function showRoleInfo() {
    var role = document.getElementById('role').value;
    var roleInfo = document.getElementById('roleInfo');
    var roleDescription = document.getElementById('roleDescription');
    
    if (role) {
        var descriptions = {
            'User': '自分のジョブと作業時間の管理ができます。',
            'Leader': 'チーム管理、メンバー予定入力、アカウント登録ができます。',
            'Admin': 'すべての機能にアクセスでき、システム全体を管理できます。'
        };
        roleDescription.textContent = descriptions[role];
        roleInfo.style.display = 'block';
    } else {
        roleInfo.style.display = 'none';
    }
}

// フォームバリデーション
function validateForm() {
    var name = document.getElementById('name').value.trim();
    var email = document.getElementById('email').value.trim();
    var password = document.getElementById('password').value;
    var role = document.getElementById('role').value;
    
    var isValid = true;
    var errorMessages = [];
    
    // 名前の検証
    if (!name) {
        errorMessages.push('名前を入力してください。');
        isValid = false;
    }
    
    // メールアドレスの検証
    if (!email) {
        errorMessages.push('メールアドレスを入力してください。');
        isValid = false;
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
        errorMessages.push('有効なメールアドレスを入力してください。');
        isValid = false;
    }
    
    // パスワードの検証
    if (!password) {
        errorMessages.push('パスワードを入力してください。');
        isValid = false;
    } else if (password.length < 12) {
        errorMessages.push('パスワードは12文字以上で入力してください。');
        isValid = false;
    }
    
    // 権限の検証
    if (!role) {
        errorMessages.push('権限を選択してください。');
        isValid = false;
    }
    
    // エラーメッセージの表示
    if (!isValid) {
        alert('入力内容に問題があります：\n' + errorMessages.join('\n'));
    }
    
    return isValid;
}

// ページ読み込み時の初期化
document.addEventListener('DOMContentLoaded', function() {
    // パスワード入力フィールドにイベントリスナーを追加
    var passwordInput = document.getElementById('password');
    if (passwordInput) {
        passwordInput.addEventListener('input', checkPasswordStrength);
    }
    
    // 権限選択フィールドにイベントリスナーを追加
    var roleSelect = document.getElementById('role');
    if (roleSelect) {
        roleSelect.addEventListener('change', showRoleInfo);
    }
    
    // フォームにバリデーションを追加
    var form = document.getElementById('registerForm');
    if (form) {
        form.addEventListener('submit', function(e) {
            if (!validateForm()) {
                e.preventDefault();
            }
        });
    }
}); 