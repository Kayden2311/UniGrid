package com.unigrid.DataAccess.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.time.Instant;
import java.util.UUID;

@Getter
@Setter
@Entity
@Table(name = "ChatMessage", schema = "dbo")
public class ChatMessage {
    @Id
    @ColumnDefault("newsequentialid()")
    @Column(name = "messageId", nullable = false)
    private UUID id;

    @Column(name = "roomId")
    private UUID roomId;

    @Column(name = "memberId")
    private UUID memberId;

    @Nationalized
    @Lob
    @Column(name = "content")
    private String content;

    @ColumnDefault("sysdatetime()")
    @Column(name = "sentAt")
    private Instant sentAt;


}