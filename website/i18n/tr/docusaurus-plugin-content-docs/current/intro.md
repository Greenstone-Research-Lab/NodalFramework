---
sidebar_position: 1
title: Nodal Framework
description: .NET için provider bağımsız ve güçlü tipli graph veri erişim çatısı.
---

# Domain modelinize ait graph veri erişimi

Nodal Framework, .NET uygulamalarına graph verisini sorgulamak ve değiştirmek için tek bir güçlü tipli model sunar. Provider paketleri bu modeli veritabanının doğal sorgu diline çevirir ve sonuçları uygulama koduna ulaşmadan önce ortak biçime getirir.

İlk provider ailesi Neo4j'yi Cypher ve havuzlanmış Bolt sürücüsüyle, TigerGraph'ı ise GSQL ve REST++ ile destekler. Domain modeli, sorgu ifadeleri, tracking kuralları ve migration niyeti provider bağımsız kalır.

## Tasarım ilkeleri

- Node ve relation modelleri sıradan POCO'lardır.
- Desteklenen işlemler veritabanının doğal yeteneklerine derlenir.
- Değerler varsayılan olarak parametreleştirilir.
- Provider yetenekleri ve sınırlamaları açıkça bildirilir.
- İhtiyaç halinde parametreli native sorgu kaçış noktası vardır.

Nodal Framework şu anda .NET 10 hedefler ve aktif pre-release geliştirme aşamasındadır.

[Başlangıç rehberi](./getting-started.md) ile devam edin.
