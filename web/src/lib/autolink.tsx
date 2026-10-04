import type { ReactNode } from 'react';

/**
 * プレーンテキストの中の URL（http と https だけ）をリンクにする（NF-INP-03、NF-INP-07）。
 * 利用者の入力は HTML として解釈せず、文字列として描画する。外部へのリンクには rel="noopener noreferrer" を付ける。
 */
const URL_PATTERN = /\bhttps?:\/\/[^\s<>"'（）「」、。]+/g;

export function splitLinks(text: string): Array<{ kind: 'text' | 'link'; value: string }> {
  const parts: Array<{ kind: 'text' | 'link'; value: string }> = [];
  let last = 0;
  for (const match of text.matchAll(URL_PATTERN)) {
    const index = match.index ?? 0;
    if (index > last) parts.push({ kind: 'text', value: text.slice(last, index) });
    let url = match[0];
    // 文末の句読点や閉じかっこはリンクに含めない
    const trailing = /[.,;:!?)\]]+$/.exec(url);
    if (trailing) url = url.slice(0, url.length - trailing[0].length);
    parts.push({ kind: 'link', value: url });
    last = index + url.length;
  }
  if (last < text.length) parts.push({ kind: 'text', value: text.slice(last) });
  return parts;
}

export function AutoLinkText({ text }: { text: string | null | undefined }): ReactNode {
  if (!text) return null;
  return (
    <>
      {splitLinks(text).map((part, i) =>
        part.kind === 'link' ? (
          <a key={i} href={part.value} target="_blank" rel="noopener noreferrer">
            {part.value}
          </a>
        ) : (
          <span key={i}>{part.value}</span>
        ),
      )}
    </>
  );
}
