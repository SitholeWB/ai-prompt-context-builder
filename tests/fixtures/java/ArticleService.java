package com.example.service;

import static com.example.utils.StringUtils.toSlug;

public class ArticleService {
    public String createArticleSlug(String title) {
        return toSlug(title);
    }
}
